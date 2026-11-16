#import "InputEngine.h"
#import "Wire.h"
#import <ApplicationServices/ApplicationServices.h>
#import <QuartzCore/QuartzCore.h>
static const int64_t MBInjected=0x4d425249444745;
static double now(void){return CACurrentMediaTime();}
static CGRect screenAt(CGPoint p){CGDirectDisplayID ids[16];uint32_t count=0;CGGetActiveDisplayList(16,ids,&count);for(uint32_t i=0;i<count;i++){CGRect r=CGDisplayBounds(ids[i]);if(CGRectContainsPoint(r,p))return r;}return CGDisplayBounds(CGMainDisplayID());}
static CGRect edgeScreen(BOOL left){CGDirectDisplayID ids[16];uint32_t count=0;CGGetActiveDisplayList(16,ids,&count);CGRect r=CGDisplayBounds(CGMainDisplayID());for(uint32_t i=0;i<count;i++){CGRect x=CGDisplayBounds(ids[i]);if(left?CGRectGetMinX(x)<CGRectGetMinX(r):CGRectGetMaxX(x)>CGRectGetMaxX(r))r=x;}return r;}
static BOOL outerEdge(CGRect r,CGPoint p,BOOL left){CGDirectDisplayID ids[16];uint32_t n=0;CGPoint beyond=CGPointMake(left?CGRectGetMinX(r)-1:CGRectGetMaxX(r),p.y);CGGetDisplaysWithPoint(beyond,16,ids,&n);return n==0;}
static BOOL portal(CGRect r,CGPoint p,BOOL left,int dx){double y=(p.y-r.origin.y)/MAX(r.size.height-1,1);return y>=.1&&y<.9&&(left?(p.x<=r.origin.x&&dx<0):(p.x>=CGRectGetMaxX(r)-1&&dx>0));}
@interface MBEdgeView:NSView
@property BOOL left;
@end
@implementation MBEdgeView
- (void)drawRect:(NSRect)dirty {
 NSColor *blue=[NSColor colorWithSRGBRed:.26 green:.39 blue:.84 alpha:1];
 NSGradient *gradient=[[NSGradient alloc]initWithStartingColor:[blue colorWithAlphaComponent:.7] endingColor:[blue colorWithAlphaComponent:0]];
 NSBezierPath *path=[NSBezierPath bezierPathWithRoundedRect:self.bounds xRadius:4 yRadius:4];[gradient drawInBezierPath:path angle:self.left?0:180];
 [blue setFill];NSRectFill(NSMakeRect(self.left?0:self.bounds.size.width-2,3,2,self.bounds.size.height-6));
}
@end
@interface MBInputEngine ()
- (CGEventRef)event:(CGEventType)type value:(CGEventRef)e;
@property MBBluetoothLink *link;
@property BOOL secondary,active,receiving,returnRequested,edgeSession,receiverLeft,stopped;
@property NSData *tx,*pending,*rx,*returning,*returnOut;
@property NSMutableData *actions,*clipboardData;
@property NSData *clipboardSession;
@property uint8_t clipboardFlow,clipboardKind;
@property uint32_t clipboardSize;
@property NSInteger clipboardBaseline;
@property NSMutableSet<NSNumber *> *keys,*buttons,*physicalKeys,*physicalButtons;
@property NSTimer *timer;
@property NSPanel *indicator;
@property double lastAck,lastInput,lastPulse,deadline,cooldown,returnSent;
@property CGPoint anchor,saved;
@property CGRect sourceScreen,receiverScreen;
@property BOOL f8Held;
@end
@implementation MBInputEngine {
 CFMachPortRef _tap;CFRunLoopSourceRef _tapSource;
}
static CGEventRef eventCallback(CGEventTapProxy proxy,CGEventType type,CGEventRef event,void *context){return [(__bridge MBInputEngine *)context event:type value:event];}
- (instancetype)initWithLink:(MBBluetoothLink *)link secondary:(BOOL)secondary {
 if((self=[super init])){_link=link;_secondary=secondary;_secondaryLeft=YES;_edgeEnabled=YES;_actions=[NSMutableData data];_keys=[NSMutableSet set];_buttons=[NSMutableSet set];_physicalKeys=[NSMutableSet set];_physicalButtons=[NSMutableSet set];}return self;
}
- (void)report:(NSString *)text{if(self.state)self.state(text);}
- (CGPoint)cursor {CGEventRef e=CGEventCreate(NULL);CGPoint p=CGEventGetLocation(e);CFRelease(e);return p;}
- (BOOL)start {
 NSDictionary *options=@{(__bridge NSString *)kAXTrustedCheckOptionPrompt:@YES};
 if(!AXIsProcessTrustedWithOptions((__bridge CFDictionaryRef)options)){[self report:@"Allow Accessibility in System Settings, then reconnect."];return NO;}
 if(!self.secondary){
  CGEventMask mask=CGEventMaskBit(kCGEventKeyDown)|CGEventMaskBit(kCGEventKeyUp)|CGEventMaskBit(kCGEventFlagsChanged)|CGEventMaskBit(kCGEventMouseMoved)|CGEventMaskBit(kCGEventLeftMouseDragged)|CGEventMaskBit(kCGEventRightMouseDragged)|CGEventMaskBit(kCGEventOtherMouseDragged)|CGEventMaskBit(kCGEventLeftMouseDown)|CGEventMaskBit(kCGEventLeftMouseUp)|CGEventMaskBit(kCGEventRightMouseDown)|CGEventMaskBit(kCGEventRightMouseUp)|CGEventMaskBit(kCGEventOtherMouseDown)|CGEventMaskBit(kCGEventOtherMouseUp)|CGEventMaskBit(kCGEventScrollWheel);
  _tap=CGEventTapCreate(kCGSessionEventTap,kCGHeadInsertEventTap,kCGEventTapOptionDefault,mask,eventCallback,(__bridge void *)self);
  if(!_tap){[self report:@"Allow Input Monitoring and Accessibility, then reconnect."];return NO;}
  _tapSource=CFMachPortCreateRunLoopSource(kCFAllocatorDefault,_tap,0);CFRunLoopAddSource(CFRunLoopGetMain(),_tapSource,kCFRunLoopCommonModes);CGEventTapEnable(_tap,true);
 }
 self.lastPulse=now();__weak MBInputEngine *weak=self;
 self.timer=[NSTimer timerWithTimeInterval:.008 repeats:YES block:^(NSTimer *t){[weak tick];}];[[NSRunLoop mainRunLoop] addTimer:self.timer forMode:NSRunLoopCommonModes];
 [self report:self.secondary?@"Secondary ready · experimental macOS":@"Main ready · F8 or screen edge to switch"];return YES;
}
- (BOOL)send:(uint8_t)kind data:(NSData *)data {if(self.stopped)return NO;if(![self.link send:kind payload:data]){[self reset];[self report:@"Send failed · local control restored"];return NO;}return YES;}
- (NSData *)session:(NSData *)session value:(int)value {NSMutableData *d=[session mutableCopy];if(value>=0){uint8_t v=value;[d appendBytes:&v length:1];}return d;}
- (BOOL)matches:(NSData *)p session:(NSData *)session length:(NSUInteger)n {return session&&p.length==n&&memcmp(p.bytes,session.bytes,16)==0;}
- (BOOL)validSession:(NSData *)p length:(NSUInteger)n {if(p.length!=n)return NO;const uint8_t *b=p.bytes;int bits=0;for(int i=0;i<16;i++)bits|=b[i];return bits!=0;}
- (void)showIndicator:(CGRect)screen left:(BOOL)left {
 [self.indicator orderOut:nil];CGFloat top=NSMaxY(NSScreen.screens.firstObject.frame);
 self.indicator=[[NSPanel alloc]initWithContentRect:NSMakeRect(left?screen.origin.x:CGRectGetMaxX(screen)-50,top-(screen.origin.y+screen.size.height*.9),50,screen.size.height*.8) styleMask:NSWindowStyleMaskBorderless|NSWindowStyleMaskNonactivatingPanel backing:NSBackingStoreBuffered defer:NO];
 self.indicator.opaque=NO;self.indicator.backgroundColor=NSColor.clearColor;self.indicator.alphaValue=.35;self.indicator.ignoresMouseEvents=YES;self.indicator.hidesOnDeactivate=NO;self.indicator.level=NSStatusWindowLevel;self.indicator.collectionBehavior=NSWindowCollectionBehaviorCanJoinAllSpaces|NSWindowCollectionBehaviorFullScreenAuxiliary;
 MBEdgeView *view=[[MBEdgeView alloc]initWithFrame:self.indicator.contentView.bounds];view.left=left;self.indicator.contentView=view;[self.indicator orderFrontRegardless];
}
- (void)reset {
 [self.indicator orderOut:nil];self.indicator=nil;
 self.active=NO;self.pending=nil;self.tx=nil;self.rx=nil;self.returning=nil;self.returnOut=nil;self.receiving=NO;self.clipboardSession=nil;self.clipboardData=nil;self.returnRequested=NO;[self.actions setLength:0];
 for(NSNumber *key in self.keys.copy){[self.keys removeObject:key];int mac=mb_mac_key(key.intValue);if(mac>=0)[self postKey:mac down:NO];}
 for(NSNumber *button in self.buttons.copy)[self postButton:button.intValue down:NO];[self.buttons removeAllObjects];self.cooldown=now()+.6;
 if(!CGPointEqualToPoint(self.anchor,CGPointZero)){CGWarpMouseCursorPosition(self.saved);self.anchor=CGPointZero;}
}
- (void)flush {if(self.tx&&self.actions.length){NSUInteger n=self.actions.length/13;NSMutableData *d=[self.tx mutableCopy];uint8_t count[]={n,n>>8};[d appendBytes:count length:2];[d appendData:self.actions];[self.actions setLength:0];[self send:13 data:d];}}
- (void)append:(uint8_t)kind a:(int)a b:(int)b c:(int)c {
 if(!self.active||!self.tx)return;if(!mb_valid_action(kind,a,b,c))return;
 if(self.actions.length>=128*13)[self flush];uint8_t bytes[13]={kind};mb_write_le(bytes+1,a);mb_write_le(bytes+5,b);mb_write_le(bytes+9,c);[self.actions appendBytes:bytes length:13];
}
- (void)toggle {
 if(self.stopped||self.secondary)return;
 if(self.active){[self returnToMain];return;}
 if(self.pending){[self send:12 data:self.pending];[self reset];return;}
 if(self.returning||self.physicalKeys.count||self.physicalButtons.count)return;
 uint8_t id[16];arc4random_buf(id,16);self.pending=[NSData dataWithBytes:id length:16];self.deadline=now()+8;self.saved=[self cursor];self.sourceScreen=screenAt(self.saved);self.clipboardBaseline=NSPasteboard.generalPasteboard.changeCount;
 [self send:10 data:[self session:self.pending value:3]];
 if(self.edgeEnabled){NSMutableData *edge=[self.pending mutableCopy];uint8_t tail[5]={self.secondaryLeft};int ratio=(int)((self.saved.y-self.sourceScreen.origin.y)*10000/MAX(self.sourceScreen.size.height-1,1));mb_write_le(tail+1,MAX(0,MIN(10000,ratio)));[edge appendBytes:tail length:5];[self send:self.edgeSession?24:26 data:edge];}
 [self sendClipboard:self.pending flow:1];[self report:@"Waiting for secondary and switch clipboard…"];
}
- (void)returnToMain {
 if(!self.tx)return;[self flush];NSData *id=self.tx;self.tx=nil;self.active=NO;self.returning=id;self.deadline=now()+8;self.clipboardBaseline=NSPasteboard.generalPasteboard.changeCount;self.clipboardSession=nil;self.clipboardData=nil;[self.indicator orderOut:nil];self.indicator=nil;CGWarpMouseCursorPosition(self.saved);self.anchor=CGPointZero;self.cooldown=now()+.6;[self send:23 data:id];[self report:@"Main active · receiving clipboard"];
}
- (void)sendClipboard:(NSData *)session flow:(uint8_t)flow {
 __weak MBInputEngine *weak=self;dispatch_after(dispatch_time(DISPATCH_TIME_NOW,80*NSEC_PER_MSEC),dispatch_get_main_queue(),^{
  MBInputEngine *s=weak;if(!s||s.stopped)return;if(flow==1?![session isEqual:s.pending]:![session isEqual:s.returnOut])return;
  NSString *text=[NSPasteboard.generalPasteboard stringForType:NSPasteboardTypeString];NSData *data=[text dataUsingEncoding:NSUTF8StringEncoding];uint8_t kind=text?1:0;if(data.length>MB_MAX_CLIPBOARD){kind=2;data=nil;}
  NSMutableData *prefix=[[s session:session value:flow] mutableCopy];NSMutableData *begin=[prefix mutableCopy];uint8_t extra[5]={kind};mb_write_le(extra+1,(uint32_t)data.length);[begin appendBytes:extra length:5];[s send:20 data:begin];
  for(NSUInteger offset=0;offset<data.length;offset+=8192){NSMutableData *chunk=[prefix mutableCopy];uint8_t n[4];mb_write_le(n,(uint32_t)offset);[chunk appendBytes:n length:4];[chunk appendData:[data subdataWithRange:NSMakeRange(offset,MIN(8192,data.length-offset))]];[s send:21 data:chunk];}
  [s send:22 data:prefix];if(flow==2)s.returnOut=nil;
 });
}
- (void)clipboard:(uint8_t)kind payload:(NSData *)p {
 if(p.length<17){[self protocolError];return;}const uint8_t *b=p.bytes;NSData *session=[p subdataWithRange:NSMakeRange(0,16)];uint8_t flow=b[16];
 if(self.secondary?!(flow==1&&[session isEqual:self.rx]&&self.receiving):!(flow==2&&[session isEqual:self.returning]))return;
 self.lastInput=now();
 if(kind==20){if(p.length!=22||b[17]>2){[self protocolError];return;}uint32_t n=mb_read_le(b+18);if(n>MB_MAX_CLIPBOARD||(b[17]!=1&&n!=0)){[self protocolError];return;}self.clipboardSession=session;self.clipboardFlow=flow;self.clipboardKind=b[17];self.clipboardSize=n;self.clipboardData=[NSMutableData data];return;}
 if(![session isEqual:self.clipboardSession]||flow!=self.clipboardFlow){[self protocolError];return;}
 if(kind==21){if(p.length<=21||p.length>8213||mb_read_le(b+17)!=self.clipboardData.length||p.length-21>self.clipboardSize-self.clipboardData.length){[self protocolError];return;}[self.clipboardData appendData:[p subdataWithRange:NSMakeRange(21,p.length-21)]];return;}
 if(p.length!=17||self.clipboardData.length!=self.clipboardSize){[self protocolError];return;}
 if(self.clipboardKind==1){NSString *text=[[NSString alloc]initWithData:self.clipboardData encoding:NSUTF8StringEncoding];if(!text){[self protocolError];return;}if(NSPasteboard.generalPasteboard.changeCount==self.clipboardBaseline){[NSPasteboard.generalPasteboard clearContents];[NSPasteboard.generalPasteboard setString:text forType:NSPasteboardTypeString];}}
 self.clipboardData=nil;self.clipboardSession=nil;
 if(self.secondary){self.receiving=NO;self.lastInput=now();[self send:11 data:[self session:self.rx value:1]];[self report:@"Receiving keyboard and mouse input"];}else{self.returning=nil;[self report:@"Main ready · clipboard received"];} 
}
- (void)handle:(uint8_t)kind payload:(NSData *)p {
 if(self.stopped)return;const uint8_t *b=p.bytes;
 if(kind==3){if(p.length!=8){[self protocolError];return;}[self send:4 data:p];return;}
 if(kind==1||kind==2||kind==4)return;
 if(kind>=20&&kind<=22){[self clipboard:kind payload:p];return;}
 if(kind==13){if(p.length<18){[self protocolError];return;}int n=b[16]|b[17]<<8;if(n<1||n>128||p.length!=(NSUInteger)(18+13*n)){[self protocolError];return;}
  for(int i=0;i<n;i++){const uint8_t *a=b+18+13*i;if(!mb_valid_action(a[0],mb_read_le(a+1),mb_read_le(a+5),mb_read_le(a+9))){[self protocolError];return;}}
  if(!self.secondary||self.receiving||![self matches:p session:self.rx length:p.length])return;self.lastInput=now();
  for(int i=0;i<n;i++){const uint8_t *a=b+18+13*i;[self inject:a[0] a:(int32_t)mb_read_le(a+1) b:(int32_t)mb_read_le(a+5) c:(int32_t)mb_read_le(a+9)];}return;}
 if(kind>=24&&kind<=26){if(p.length!=21||b[16]>1||mb_read_le(b+17)>10000){[self protocolError];return;}
  if((kind==24||kind==26)&&self.secondary&&self.receiving&&[self matches:p session:self.rx length:21]){self.edgeSession=YES;self.receiverLeft=!b[16];self.receiverScreen=edgeScreen(self.receiverLeft);self.cooldown=now()+.6;if(kind==24){CGRect r=self.receiverScreen;CGWarpMouseCursorPosition(CGPointMake(self.receiverLeft?r.origin.x+3:CGRectGetMaxX(r)-4,r.origin.y+mb_read_le(b+17)*(r.size.height-1)/10000.0));}}
  if(kind==25&&!self.secondary&&self.active&&self.edgeEnabled&&[self matches:p session:self.tx length:21]&&b[16]!=self.secondaryLeft&&!self.physicalKeys.count&&!self.physicalButtons.count){int ratio=mb_read_le(b+17);[self returnToMain];CGRect r=self.sourceScreen;CGWarpMouseCursorPosition(CGPointMake(self.secondaryLeft?r.origin.x+3:CGRectGetMaxX(r)-4,r.origin.y+ratio*(r.size.height-1)/10000.0));}return;}
 if(kind==10||kind==11){if(![self validSession:p length:17]){[self protocolError];return;}}
 else if(![self validSession:p length:16]){[self protocolError];return;}
 NSData *id=[p subdataWithRange:NSMakeRange(0,16)];
 switch(kind){
 case 10:if(self.secondary&&!self.rx&&b[16]==3){[self.indicator orderOut:nil];self.indicator=nil;self.rx=id;self.receiving=YES;self.edgeSession=NO;self.deadline=now()+8;self.lastInput=now();self.clipboardBaseline=NSPasteboard.generalPasteboard.changeCount;}else [self send:11 data:[self session:id value:0]];break;
 case 11:if(self.secondary||b[16]>1){[self protocolError];return;}if(![id isEqual:self.pending])break;if(!b[16]){[self reset];[self report:@"Control rejected. Check role/protocol version."];break;}
  if(self.physicalKeys.count||self.physicalButtons.count){[self send:12 data:id];[self reset];break;}
  self.tx=id;self.pending=nil;self.active=YES;self.lastAck=now();self.anchor=CGPointMake(CGRectGetMidX(screenAt(self.saved)),CGRectGetMidY(screenAt(self.saved)));CGWarpMouseCursorPosition(self.anchor);if(self.edgeEnabled)[self showIndicator:self.sourceScreen left:self.secondaryLeft];self.cooldown=now()+.6;[self report:@"Controlling secondary · F8 or facing edge to return"];break;
 case 12:if([id isEqual:self.tx]||[id isEqual:self.pending]||[id isEqual:self.rx]||[id isEqual:self.returning]){[self reset];[self report:@"Control session ended · local control restored"];}break;
 case 14:if(self.secondary&&[id isEqual:self.rx]){self.lastInput=now();[self send:15 data:id];}break;
 case 15:if([id isEqual:self.tx])self.lastAck=now();break;
 case 16:[self send:17 data:id];break;
 case 17:break;
 case 23:if(self.secondary&&[id isEqual:self.rx]){BOOL show=self.edgeSession;CGRect screen=self.receiverScreen;BOOL left=self.receiverLeft;[self reset];if(show)[self showIndicator:screen left:left];self.returnOut=id;[self sendClipboard:id flow:2];[self report:@"Returned to main · sending clipboard"];}break;
 default:[self protocolError];break;
 }
}
- (void)protocolError {[self report:@"Invalid protocol data. Connection stopped."];[self reset];[self.link stop];}
- (void)tick {
 if(self.stopped)return;double t=now();
 if(!self.link.secure){[self report:@"Bluetooth encryption unavailable; stopped."];[self reset];[self.link stop];return;}
 if((self.pending||self.returning||self.receiving)&&t>self.deadline){NSData *id=self.pending?:self.returning?:self.rx;if(id)[self send:12 data:id];[self reset];[self report:@"Switch timed out · local control restored"];} 
 if(self.active){if(t-self.lastAck>2){[self send:12 data:self.tx];[self reset];[self report:@"Control heartbeat lost · local control restored"];return;}[self flush];}
 if(self.rx&&!self.receiving&&t-self.lastInput>2){[self send:12 data:self.rx];[self reset];[self report:@"Input heartbeat expired · keys released"];} 
 if(t-self.lastPulse>.5){self.lastPulse=t;if(self.tx)[self send:14 data:self.tx];else if(!self.secondary){uint8_t pulse[16]={1};[self send:16 data:[NSData dataWithBytes:pulse length:16]];}}
}
- (void)post:(CGEventRef)e {if(!e)return;CGEventSetIntegerValueField(e,kCGEventSourceUserData,MBInjected);CGEventPost(kCGHIDEventTap,e);CFRelease(e);}
- (CGEventFlags)injectedFlags {CGEventFlags flags=0;for(NSNumber *vk in self.keys){switch(mb_mac_key(vk.intValue)){case 54:case 55:flags|=kCGEventFlagMaskCommand;break;case 59:case 62:flags|=kCGEventFlagMaskControl;break;case 56:case 60:flags|=kCGEventFlagMaskShift;break;case 58:case 61:flags|=kCGEventFlagMaskAlternate;break;}}return flags;}
- (void)postKey:(int)key down:(BOOL)down {CGEventRef e=CGEventCreateKeyboardEvent(NULL,(CGKeyCode)key,down);CGEventSetFlags(e,[self injectedFlags]);[self post:e];}
- (void)postButton:(int)button down:(BOOL)down {CGMouseButton b=button==1?kCGMouseButtonLeft:button==2?kCGMouseButtonRight:(CGMouseButton)(button-1);CGEventType t=button==1?(down?kCGEventLeftMouseDown:kCGEventLeftMouseUp):button==2?(down?kCGEventRightMouseDown:kCGEventRightMouseUp):(down?kCGEventOtherMouseDown:kCGEventOtherMouseUp);[self post:CGEventCreateMouseEvent(NULL,t,[self cursor],b)];}
- (void)inject:(uint8_t)kind a:(int)a b:(int)b c:(int)c {
 if(kind==1){int key=mb_mac_key(a);if(key<0)return;BOOL down=(c&1)==0;if(down)[self.keys addObject:@(a)];else [self.keys removeObject:@(a)];[self postKey:key down:down];return;}
 if(kind==3){if(b)[self.buttons addObject:@(a)];else [self.buttons removeObject:@(a)];[self postButton:a down:b];return;}
 if(kind==4){[self post:CGEventCreateScrollWheelEvent(NULL,kCGScrollEventUnitLine,2,b?0:a/120,b?a/120:0)];return;}
 CGPoint old=[self cursor],p=CGPointMake(old.x+a,old.y+b);CGDirectDisplayID displays[16];uint32_t count=0;CGGetDisplaysWithPoint(p,16,displays,&count);if(!count){CGRect r=screenAt(old);p.x=MAX(r.origin.x,MIN(CGRectGetMaxX(r)-1,p.x));p.y=MAX(r.origin.y,MIN(CGRectGetMaxY(r)-1,p.y));}
 CGEventType type=kCGEventMouseMoved;CGMouseButton button=kCGMouseButtonLeft;if([self.buttons containsObject:@1])type=kCGEventLeftMouseDragged;else if([self.buttons containsObject:@2]){type=kCGEventRightMouseDragged;button=kCGMouseButtonRight;}
 CGEventRef e=CGEventCreateMouseEvent(NULL,type,p,button);CGEventSetIntegerValueField(e,kCGMouseEventDeltaX,a);CGEventSetIntegerValueField(e,kCGMouseEventDeltaY,b);CGEventSetFlags(e,[self injectedFlags]);[self post:e];
 CGRect r=screenAt(p);if(self.edgeSession&&!self.keys.count&&!self.buttons.count&&now()>self.cooldown&&(!self.returnRequested||now()-self.returnSent>.5)&&portal(r,p,self.receiverLeft,a)&&outerEdge(r,p,self.receiverLeft)){
 self.returnRequested=YES;self.returnSent=now();NSMutableData *d=[self.rx mutableCopy];uint8_t tail[5]={self.receiverLeft};mb_write_le(tail+1,(int)((p.y-r.origin.y)*10000/MAX(r.size.height-1,1)));[d appendBytes:tail length:5];[self send:25 data:d];}
}
- (CGEventRef)event:(CGEventType)type value:(CGEventRef)e {
 if(type==kCGEventTapDisabledByTimeout||type==kCGEventTapDisabledByUserInput){NSData *id=self.tx?:self.pending;if(id)[self send:12 data:id];[self reset];if(_tap)CGEventTapEnable(_tap,true);return e;}
 if(self.stopped||CGEventGetIntegerValueField(e,kCGEventSourceUserData)==MBInjected)return e;
 if(type==kCGEventKeyDown||type==kCGEventKeyUp||type==kCGEventFlagsChanged){unsigned key=(unsigned)CGEventGetIntegerValueField(e,kCGKeyboardEventKeycode);CGEventFlags flags=CGEventGetFlags(e);BOOL down=type==kCGEventKeyDown;
  if(type==kCGEventFlagsChanged)down=CGEventSourceKeyState(kCGEventSourceStateCombinedSessionState,(CGKeyCode)key);
  if(key==100&&!(flags&(kCGEventFlagMaskCommand|kCGEventFlagMaskControl|kCGEventFlagMaskAlternate|kCGEventFlagMaskShift))){if(type==kCGEventKeyDown)self.f8Held=YES;else if(type==kCGEventKeyUp&&self.f8Held){self.f8Held=NO;dispatch_async(dispatch_get_main_queue(),^{self.edgeSession=NO;[self toggle];});}return NULL;}
  if(down)[self.physicalKeys addObject:@(key)];else [self.physicalKeys removeObject:@(key)];int vk=mb_windows_key(key);if(self.active){if(vk)[self append:1 a:vk b:0 c:down?0:1];return NULL;}return e;
 }
 int button=(int)CGEventGetIntegerValueField(e,kCGMouseEventButtonNumber)+1;BOOL buttonDown=type==kCGEventLeftMouseDown||type==kCGEventRightMouseDown||type==kCGEventOtherMouseDown;BOOL buttonUp=type==kCGEventLeftMouseUp||type==kCGEventRightMouseUp||type==kCGEventOtherMouseUp;
 if(buttonDown)[self.physicalButtons addObject:@(button)];if(buttonUp)[self.physicalButtons removeObject:@(button)];
 BOOL move=type==kCGEventMouseMoved||type==kCGEventLeftMouseDragged||type==kCGEventRightMouseDragged||type==kCGEventOtherMouseDragged;
 if(!self.active){if(move&&self.edgeEnabled&&!self.pending&&!self.returning&&now()>self.cooldown&&!self.physicalKeys.count&&!self.physicalButtons.count){CGPoint p=CGEventGetLocation(e);int dx=(int)CGEventGetIntegerValueField(e,kCGMouseEventDeltaX);CGRect r=screenAt(p);if(portal(r,p,self.secondaryLeft,dx)&&outerEdge(r,p,self.secondaryLeft)){self.cooldown=now()+.75;dispatch_async(dispatch_get_main_queue(),^{self.edgeSession=YES;[self toggle];});}}return e;}
 if(move){int dx=(int)CGEventGetIntegerValueField(e,kCGMouseEventDeltaX),dy=(int)CGEventGetIntegerValueField(e,kCGMouseEventDeltaY);[self append:2 a:MAX(-32767,MIN(32767,dx)) b:MAX(-32767,MIN(32767,dy)) c:0];return NULL;}
 if(buttonDown||buttonUp){if(button<=5)[self append:3 a:button b:buttonDown c:0];return NULL;}
 if(type==kCGEventScrollWheel){int y=(int)CGEventGetIntegerValueField(e,kCGScrollWheelEventDeltaAxis1),x=(int)CGEventGetIntegerValueField(e,kCGScrollWheelEventDeltaAxis2);if(y)[self append:4 a:MAX(-32767,MIN(32767,y*120)) b:0 c:0];if(x)[self append:4 a:MAX(-32767,MIN(32767,x*120)) b:1 c:0];return NULL;}return e;
}
- (void)stop {if(self.stopped)return;NSData *id=self.tx?:self.pending?:self.rx;if(id)[self send:12 data:id];[self reset];self.stopped=YES;[self.timer invalidate];self.timer=nil;if(_tap){CGEventTapEnable(_tap,false);CFMachPortInvalidate(_tap);CFRelease(_tap);_tap=NULL;}if(_tapSource){CFRunLoopRemoveSource(CFRunLoopGetMain(),_tapSource,kCFRunLoopCommonModes);CFRelease(_tapSource);_tapSource=NULL;}}
- (void)dealloc {if(_tap){CFMachPortInvalidate(_tap);CFRelease(_tap);}if(_tapSource)CFRelease(_tapSource);}
@end
