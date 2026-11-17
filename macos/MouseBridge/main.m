#import <Cocoa/Cocoa.h>
#import "BluetoothLink.h"
#import "InputEngine.h"
@interface MBApp : NSObject<NSApplicationDelegate,NSWindowDelegate>
@property NSWindow *window;
@property NSTextField *status,*inputState;
@property NSPopUpButton *devices,*side;
@property NSButton *edge,*mainButton,*secondaryButton;
@property MBBluetoothLink *link;
@property MBInputEngine *engine;
@property NSArray *paired;
@property BOOL secondary;
@property NSMutableString *diagnostics;
@end
@implementation MBApp
- (NSTextField *)label:(NSString *)text size:(CGFloat)size {NSTextField *v=[NSTextField labelWithString:text];v.font=[NSFont systemFontOfSize:size weight:size>18?NSFontWeightSemibold:NSFontWeightRegular];v.lineBreakMode=NSLineBreakByWordWrapping;v.maximumNumberOfLines=0;v.preferredMaxLayoutWidth=600;return v;}
- (NSButton *)button:(NSString *)title action:(SEL)action {NSButton *b=[NSButton buttonWithTitle:title target:self action:action];b.bezelStyle=NSBezelStyleRounded;return b;}
- (void)applicationDidFinishLaunching:(NSNotification *)notification {
 self.diagnostics=[NSMutableString stringWithFormat:@"MouseBridge macOS 0.3.5 · %@\n",NSProcessInfo.processInfo.operatingSystemVersionString];
 [NSApp setActivationPolicy:NSApplicationActivationPolicyRegular];NSMenu *menu=[NSMenu new];NSMenuItem *app=[NSMenuItem new];[menu addItem:app];NSMenu *sub=[NSMenu new];[sub addItemWithTitle:@"Quit MouseBridge" action:@selector(terminate:) keyEquivalent:@"q"];app.submenu=sub;NSApp.mainMenu=menu;
 self.window=[[NSWindow alloc]initWithContentRect:NSMakeRect(0,0,680,760) styleMask:NSWindowStyleMaskTitled|NSWindowStyleMaskClosable|NSWindowStyleMaskMiniaturizable|NSWindowStyleMaskResizable backing:NSBackingStoreBuffered defer:NO];self.window.title=@"MouseBridge 0.3.5 · macOS experimental";self.window.delegate=self;self.window.minSize=NSMakeSize(680,600);[self.window center];
 NSScrollView *scroll=[[NSScrollView alloc]initWithFrame:self.window.contentView.bounds];scroll.autoresizingMask=NSViewWidthSizable|NSViewHeightSizable;scroll.hasVerticalScroller=YES;scroll.drawsBackground=NO;[self.window.contentView addSubview:scroll];
 NSView *document=[NSView new];document.translatesAutoresizingMaskIntoConstraints=NO;scroll.documentView=document;
 NSStackView *stack=[NSStackView new];stack.orientation=NSUserInterfaceLayoutOrientationVertical;stack.alignment=NSLayoutAttributeLeading;stack.spacing=16;stack.translatesAutoresizingMaskIntoConstraints=NO;[document addSubview:stack];
 [NSLayoutConstraint activateConstraints:@[[document.widthAnchor constraintEqualToAnchor:scroll.contentView.widthAnchor],[stack.leadingAnchor constraintEqualToAnchor:document.leadingAnchor constant:28],[stack.trailingAnchor constraintEqualToAnchor:document.trailingAnchor constant:-28],[stack.topAnchor constraintEqualToAnchor:document.topAnchor constant:28],[stack.bottomAnchor constraintEqualToAnchor:document.bottomAnchor constant:-28]]];
 [stack addArrangedSubview:[self label:@"MouseBridge" size:28]];[stack addArrangedSubview:[self label:@"One keyboard and mouse. Bluetooth. No shared network." size:14]];
 [stack addArrangedSubview:[self label:@"macOS preview · built for Mac–Windows and Mac–Mac. Bluetooth and input behavior require real-device testing." size:12]];
 NSStackView *roles=[NSStackView new];roles.spacing=12;self.mainButton=[self button:@"Main PC" action:@selector(main:)];self.secondaryButton=[self button:@"Secondary PC" action:@selector(secondary:)];[roles addArrangedSubview:self.mainButton];[roles addArrangedSubview:self.secondaryButton];[roles addArrangedSubview:[self button:@"Stop" action:@selector(stop:)]];[stack addArrangedSubview:roles];
 self.status=[self label:@"Choose a role to begin." size:15];[stack addArrangedSubview:self.status];self.inputState=[self label:@"Grant Accessibility and Input Monitoring in System Settings when prompted." size:13];[stack addArrangedSubview:self.inputState];
 NSStackView *connection=[NSStackView new];connection.spacing=8;self.devices=[NSPopUpButton new];[connection addArrangedSubview:self.devices];[connection addArrangedSubview:[self button:@"Refresh paired devices" action:@selector(refresh:)]];[connection addArrangedSubview:[self button:@"Connect / reconnect" action:@selector(connect:)]];[stack addArrangedSubview:connection];
 [stack addArrangedSubview:[self label:@"Screen arrangement (on Main)" size:16]];self.side=[NSPopUpButton new];[self.side addItemsWithTitles:@[@"Secondary on left  ←  Main",@"Main  →  Secondary on right"]];self.side.target=self;self.side.action=@selector(settings:);[self.side selectItemAtIndex:[NSUserDefaults.standardUserDefaults boolForKey:@"secondaryRight"]?1:0];[stack addArrangedSubview:self.side];
 self.edge=[NSButton checkboxWithTitle:@"Switch through middle 80% of facing screen edge" target:self action:@selector(settings:)];self.edge.state=[NSUserDefaults.standardUserDefaults boolForKey:@"edgesDisabled"]?NSControlStateValueOff:NSControlStateValueOn;[stack addArrangedSubview:self.edge];
 [stack addArrangedSubview:[self label:@"F8 switches control in either direction. On some Macs use Fn+F8 or enable standard function keys. Copy → switch → paste transfers plain text (up to 256KiB). Wait for clipboard status before pasting." size:13]];
 [stack addArrangedSubview:[self button:@"Permissions and Bluetooth settings" action:@selector(permissions:)]];
 [stack addArrangedSubview:[self button:@"Copy connection diagnostics" action:@selector(copyDiagnostics:)]];
 [stack addArrangedSubview:[self label:@"Preview limits: F8 fixed; no auto reconnect, custom shortcut UI or indicator customization yet. Layout/IME behavior differs by app. Stop sharing before changing settings. Keep both apps running; minimizing is supported by design, not yet hardware-verified." size:12]];
 [self refresh:nil];[self.window makeKeyAndOrderFront:nil];[NSApp activateIgnoringOtherApps:YES];
}
- (void)refresh:(id)sender {self.paired=[MBBluetoothLink pairedDevices];[self.devices removeAllItems];for(IOBluetoothDevice *d in self.paired)[self.devices addItemWithTitle:[d nameOrAddress]?:@"Bluetooth device"];if(!self.paired.count)[self.devices addItemWithTitle:@"Pair a computer in Bluetooth settings"];} 
- (void)makeLink {
 [self.engine stop];self.engine=nil;[self.link stop];self.link=[MBBluetoothLink new];__weak MBApp *weak=self;
 self.link.diagnostic=^(NSString *text){MBApp *s=weak;if(!s)return;if(s.diagnostics.length>32000)[s.diagnostics deleteCharactersInRange:NSMakeRange(0,16000)];[s.diagnostics appendFormat:@"%@ %@\n",[NSDate date],text];};
 self.link.state=^(NSString *text){weak.status.stringValue=text;};
 self.link.closed=^{[weak.engine stop];weak.engine=nil;weak.inputState.stringValue=@"Disconnected · local input restored";};
 self.link.connected=^{MBApp *s=weak;if(!s)return;
  if(s.secondary){
  if(s.link.diagnostic)s.link.diagnostic(@"Presenting incoming control approval dialog on main thread.");
  [NSApp activateIgnoringOtherApps:YES];[s.window makeKeyAndOrderFront:nil];
  NSAlert *alert=[NSAlert new];alert.messageText=@"Allow this paired computer to control your Mac?";alert.informativeText=[NSString stringWithFormat:@"%@ can send keyboard/mouse input and text clipboard. Only accept your own trusted main computer. Stop ends sharing.",s.link.peerName];[alert addButtonWithTitle:@"Allow this connection"];[alert addButtonWithTitle:@"Reject"];NSModalResponse response=[alert runModal];
  if(s.link.diagnostic)s.link.diagnostic(response==NSAlertFirstButtonReturn?@"Incoming control approved by user.":@"Incoming control rejected by user.");
  if(response!=NSAlertFirstButtonReturn){[s.link stop];return;}}
  s.engine=[[MBInputEngine alloc]initWithLink:s.link secondary:s.secondary];s.engine.state=^(NSString *text){weak.inputState.stringValue=text;};s.engine.secondaryLeft=s.side.indexOfSelectedItem==0;s.engine.edgeEnabled=s.edge.state==NSControlStateValueOn;
  if(![s.engine start]){NSString *reason=s.inputState.stringValue;[s.engine stop];s.engine=nil;[s.link stop];s.inputState.stringValue=reason;}else{[s.link startReceiving];}
 };
 self.link.message=^(uint8_t kind,NSData *payload){[weak.engine handle:kind payload:payload];};
}
- (void)main:(id)sender {self.secondary=NO;[self makeLink];self.status.stringValue=@"Main selected · select the paired secondary and Connect";}
- (void)secondary:(id)sender {self.secondary=YES;[self makeLink];[self.link listen];}
- (void)connect:(id)sender {if(self.secondary){[self secondary:nil];return;}NSInteger index=self.devices.indexOfSelectedItem;if(index<0||(NSUInteger)index>=self.paired.count)return;IOBluetoothDevice *d=self.paired[index];[self makeLink];[self.link connect:d];}
- (void)stop:(id)sender {[self.engine stop];self.engine=nil;[self.link stop];self.status.stringValue=@"Stopped · choose a role to reconnect";}
- (void)settings:(id)sender {if(self.engine){[self stop:nil];self.status.stringValue=@"Screen settings changed · reconnect to apply";}[NSUserDefaults.standardUserDefaults setBool:self.side.indexOfSelectedItem==1 forKey:@"secondaryRight"];[NSUserDefaults.standardUserDefaults setBool:self.edge.state!=NSControlStateValueOn forKey:@"edgesDisabled"];} 
- (void)copyDiagnostics:(id)sender {[NSPasteboard.generalPasteboard clearContents];[NSPasteboard.generalPasteboard setString:self.diagnostics?:@"No connection diagnostics yet." forType:NSPasteboardTypeString];self.inputState.stringValue=@"Connection diagnostics copied (no keys, clipboard text or device addresses).";}
- (void)permissions:(id)sender {[[NSWorkspace sharedWorkspace]openURL:[NSURL URLWithString:@"x-apple.systempreferences:com.apple.preference.security?Privacy_Accessibility"]];}
- (BOOL)applicationShouldTerminateAfterLastWindowClosed:(NSApplication *)sender{return YES;}
- (void)applicationWillTerminate:(NSNotification *)notification {[self.engine stop];[self.link stop];}
@end
int main(int argc,const char **argv){@autoreleasepool{NSApplication *app=NSApplication.sharedApplication;MBApp *delegate=[MBApp new];app.delegate=delegate;[app run];}return 0;}
