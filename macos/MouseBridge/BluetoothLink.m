#import "BluetoothLink.h"
#import "Wire.h"
@class MBSDPRequest,MBIncomingRequest;
@interface MBBluetoothLink ()
@property IOBluetoothRFCOMMChannel *channel;
@property IOBluetoothSDPServiceRecord *record;
@property IOBluetoothUserNotification *notification;
@property IOBluetoothDevice *candidate;
@property NSMutableData *received;
@property NSMutableArray<NSData *> *outgoing;
@property NSData *inflight;
@property NSUInteger offset,writeLength;
@property BOOL writing,stopping,baseband,searching,opening,attached;
@property NSUInteger generation;
@property NSTimer *connectionTimer;
@property NSTimeInterval phaseStarted;
@property NSTimeInterval discoveryTimeout,openTimeout;
@property MBSDPRequest *query;
@property MBIncomingRequest *incoming;
@property BOOL listening,receiveReady;
@property BluetoothRFCOMMChannelID serverChannel;
@property NSMutableArray<NSDictionary *> *pendingIncoming;
@property NSUInteger pendingBytes;
- (void)opened:(IOBluetoothUserNotification *)notification channel:(IOBluetoothRFCOMMChannel *)channel generation:(NSUInteger)generation;
- (void)queryComplete:(IOBluetoothDevice *)device status:(IOReturn)status generation:(NSUInteger)generation;
- (void)basebandComplete:(IOBluetoothDevice *)device status:(IOReturn)status generation:(NSUInteger)generation;
@end
// Each asynchronous SDP request carries its own generation. Old callbacks cannot advance a reconnect.
@interface MBSDPRequest:NSObject
@property(weak) MBBluetoothLink *owner;
@property NSUInteger generation;
@end
@implementation MBSDPRequest
- (void)connectionComplete:(IOBluetoothDevice *)device status:(IOReturn)status {
 MBBluetoothLink *owner=self.owner;NSUInteger generation=self.generation;
 dispatch_async(dispatch_get_main_queue(),^{[owner basebandComplete:device status:status generation:generation];});
}
- (void)sdpQueryComplete:(IOBluetoothDevice *)device status:(IOReturn)status {
 MBBluetoothLink *owner=self.owner;NSUInteger generation=self.generation;
 dispatch_async(dispatch_get_main_queue(),^{[owner queryComplete:device status:status generation:generation];});
}
@end
@interface MBIncomingRequest:NSObject
@property(weak) MBBluetoothLink *owner;
@property NSUInteger generation;
@end
@implementation MBIncomingRequest
- (void)opened:(IOBluetoothUserNotification *)notification channel:(IOBluetoothRFCOMMChannel *)channel {
 MBBluetoothLink *owner=self.owner;NSUInteger generation=self.generation;
 if(NSThread.isMainThread)[owner opened:notification channel:channel generation:generation];
 else dispatch_async(dispatch_get_main_queue(),^{[owner opened:notification channel:channel generation:generation];});
}
@end
@implementation MBBluetoothLink
+ (IOBluetoothSDPUUID *)serviceUUID {const unsigned char bytes[]={0x9d,0x96,0xd1,0xbd,0x27,0x86,0x4b,0x5d,0x97,0xe7,0xcf,0x84,0x39,0x48,0x43,0xaf};return [IOBluetoothSDPUUID uuidWithBytes:bytes length:16];}
+ (NSArray *)pairedDevices {return [IOBluetoothDevice pairedDevices]?:@[];}
- (instancetype)init {return [self initWithDiscoveryTimeout:20 openTimeout:15];}
- (instancetype)initWithDiscoveryTimeout:(NSTimeInterval)discoveryTimeout openTimeout:(NSTimeInterval)openTimeout {
 if((self=[super init])){_received=[NSMutableData data];_outgoing=[NSMutableArray array];_pendingIncoming=[NSMutableArray array];_discoveryTimeout=MAX(discoveryTimeout,.02);_openTimeout=MAX(openTimeout,.02);}return self;
}
- (NSString *)peerName{return [[self.channel getDevice] nameOrAddress]?:@"Paired computer";}
- (BOOL)ready{return self.channel!=nil&&[self.channel isOpen];}
- (void)trace:(NSString *)text{if(self.diagnostic)self.diagnostic(text);}
- (void)report:(NSString *)text{[self trace:text];if(self.state)self.state(text);}
- (void)finishWithError:(NSString *)text {[self stop];[self report:text];}
- (BOOL)sameCandidate:(IOBluetoothDevice *)device {
 NSString *expected=self.candidate.addressString,*actual=device.addressString;
 return expected.length>0&&actual.length>0&&[expected caseInsensitiveCompare:actual]==NSOrderedSame;
}
- (void)watchConnection {
 [self.connectionTimer invalidate];__weak MBBluetoothLink *weak=self;
 self.connectionTimer=[NSTimer timerWithTimeInterval:.25 repeats:YES block:^(NSTimer *timer){[weak checkConnection];}];
 [[NSRunLoop mainRunLoop]addTimer:self.connectionTimer forMode:NSRunLoopCommonModes];
}
- (void)checkConnection {
 if(self.stopping)return;NSTimeInterval elapsed=NSProcessInfo.processInfo.systemUptime-self.phaseStarted;
 if(self.baseband){
  if(self.candidate.isConnected){[self trace:@"Baseband isConnected=YES (poll); proceeding to SDP."];[self beginDiscovery];return;}
  if(elapsed>=self.openTimeout)[self finishWithError:@"Bluetooth baseband connection timed out BEFORE service discovery. Keep Windows awake with Bluetooth on, then reconnect. Copy diagnostics."];
  return;
 }
 if(self.searching){
  if(elapsed>=self.discoveryTimeout){[self trace:[NSString stringWithFormat:@"SDP deadline: basebandConnected=%@, cachedServices=%lu",self.candidate.isConnected?@"YES":@"NO",(unsigned long)self.candidate.services.count]];[self finishWithError:@"Service discovery timed out. Check Windows Secondary is running, then reconnect. Copy diagnostics for details."];return;}
  if(elapsed>=2){IOBluetoothSDPServiceRecord *record=[self.candidate getServiceRecordForUUID:[MBBluetoothLink serviceUUID]];if(record){[self trace:@"SDP callback pending, matching cached service found; trying its RFCOMM channel."];[self openService:record device:self.candidate];}}
 }
 else if(self.opening&&elapsed>=self.openTimeout)[self finishWithError:@"RFCOMM connection timed out. Keep Windows Secondary running and reconnect."];
}
+ (NSDictionary *)serviceDictionary {
 // NSNumber shortcuts encode uint32 in Apple's SDP dictionary API. RFCOMM CN must be uint8.
 NSDictionary *channel=@{@"DataElementType":@1,@"DataElementSize":@1,@"DataElementValue":@3};
 return @{@"0001 - ServiceClassIDList":@[[self serviceUUID]],
          @"0004 - ProtocolDescriptorList":@[@[[IOBluetoothSDPUUID uuid16:0x0100]],@[[IOBluetoothSDPUUID uuid16:0x0003],channel]],
          @"0005 - BrowseGroupList":@[[IOBluetoothSDPUUID uuid16:0x1002]],
          @"0100 - ServiceName":@"MouseBridge",@"LocalAttributes":@{@"Persistent":@NO}};
}
- (IOBluetoothSDPServiceRecord *)publishRecord:(NSDictionary *)dictionary {return [IOBluetoothSDPServiceRecord publishedServiceRecordWithDictionary:dictionary];}
- (IOBluetoothUserNotification *)registerIncoming:(id)target {
 // Receive the global notification; application-side guards below accept only our incoming channel.
 return [IOBluetoothRFCOMMChannel registerForChannelOpenNotifications:target selector:@selector(opened:channel:)];
}
- (void)listen {
 [self stop];self.stopping=NO;[self traceControllerState];
 NSDictionary *dict=[MBBluetoothLink serviceDictionary];
 self.record=[self publishRecord:dict];BluetoothRFCOMMChannelID channel=0;
 if(!self.record||[self.record getRFCOMMChannelID:&channel]!=kIOReturnSuccess){[self report:@"Cannot advertise RFCOMM. Check Bluetooth permission and radio."];[self stop];return;}
 self.serverChannel=channel;self.listening=YES;
 MBIncomingRequest *incoming=[MBIncomingRequest new];incoming.owner=self;incoming.generation=self.generation;self.incoming=incoming;
 self.notification=[self registerIncoming:incoming];
 if(!self.notification){[self finishWithError:@"Cannot register RFCOMM incoming notification. Copy diagnostics."];return;}
 [self trace:[NSString stringWithFormat:@"RFCOMM notification registered: unfiltered; strict channel/direction guard; mainThread=%@",NSThread.isMainThread?@"YES":@"NO"]];
 [self trace:[NSString stringWithFormat:@"SDP service published: channel=%u, channel parameter=uint8, public browse group enabled",(unsigned)channel]];
 [self report:@"Secondary ready. Pair in system Bluetooth settings first."];
}
- (void)traceControllerState {
 IOBluetoothHostController *controller=[IOBluetoothHostController defaultController];
 [self trace:[NSString stringWithFormat:@"Bluetooth authorization=%ld (0=notDetermined, 1=restricted, 2=denied, 3=allowed)",(long)CBManager.authorization]];
 [self trace:[NSString stringWithFormat:@"Controller present=%@; powerState=%ld",controller?@"YES":@"NO",controller?(long)controller.powerState:-1L]];
}
- (void)connect:(IOBluetoothDevice *)device {
 [self stop];self.stopping=NO;if(![device isPaired]){[self report:@"Pair the computers in system Bluetooth settings first."];return;}
 self.candidate=device;
 MBSDPRequest *query=[MBSDPRequest new];query.owner=self;query.generation=self.generation;self.query=query;
 [self traceControllerState];
 [self trace:[NSString stringWithFormat:@"Connection attempt %lu; paired=YES; basebandConnected=%@; cachedServices=%lu",(unsigned long)self.generation,device.isConnected?@"YES":@"NO",(unsigned long)device.services.count]];
 if(device.isConnected){[self beginDiscovery];return;}
 self.baseband=YES;self.phaseStarted=NSProcessInfo.processInfo.systemUptime;
 [self report:@"Opening authenticated Bluetooth baseband connection…"];[self watchConnection];
 IOReturn err=[device openConnection:query withPageTimeout:0x4000 authenticationRequired:YES];
 [self trace:[NSString stringWithFormat:@"openConnection(authRequired=YES) returned 0x%08x",(unsigned)err]];
 // Connection-exists return codes differ between OS versions; trust the reported connected state.
 if(err!=kIOReturnSuccess){
  if(device.isConnected)[self beginDiscovery];
  else [self finishWithError:[NSString stringWithFormat:@"Bluetooth baseband connection could not start (0x%08x), BEFORE service discovery. Copy diagnostics.",(unsigned)err]];
 }
}
- (void)basebandComplete:(IOBluetoothDevice *)device status:(IOReturn)status generation:(NSUInteger)generation {
 if(self.stopping||generation!=self.generation||!self.baseband)return;
 if(![self sameCandidate:device]){[self trace:@"Ignored baseband callback from a different device address."];return;}
 [self trace:[NSString stringWithFormat:@"Baseband callback: status=0x%08x; connected=%@",(unsigned)status,device.isConnected?@"YES":@"NO"]];
 if(status!=kIOReturnSuccess&&!device.isConnected){[self finishWithError:[NSString stringWithFormat:@"Bluetooth baseband connection failed (0x%08x), BEFORE service discovery. Copy diagnostics.",(unsigned)status]];return;}
 if(!device.isConnected){
  [self report:@"Waiting for Bluetooth connection confirmation…"];
  [self trace:@"Baseband callback succeeded but isConnected=NO; retaining baseband deadline, SDP has NOT started."];
  return;
 }
 self.candidate=device; // Prefer the connected callback wrapper for the verified same address.
 [self beginDiscovery];
}
- (void)beginDiscovery {
 if(self.stopping||self.searching||self.opening||self.attached)return;
 if(!self.candidate.isConnected){[self trace:@"SDP deferred: baseband connection is not confirmed."];return;}
 self.baseband=NO;self.searching=YES;self.phaseStarted=NSProcessInfo.processInfo.systemUptime;
 [self report:@"Discovering MouseBridge service…"];
 [self trace:[NSString stringWithFormat:@"SDP attempt %lu started AFTER baseband stage; full service query; limit=%.1fs",(unsigned long)self.generation,self.discoveryTimeout]];
 [self watchConnection];
 IOReturn err=[self.candidate performSDPQuery:self.query];
 [self trace:[NSString stringWithFormat:@"performSDPQuery returned 0x%08x",(unsigned)err]];
 if(err!=kIOReturnSuccess)[self finishWithError:[NSString stringWithFormat:@"Service discovery could not start (0x%08x). Copy diagnostics.",(unsigned)err]];
}
- (void)queryComplete:(IOBluetoothDevice *)device status:(IOReturn)status generation:(NSUInteger)generation {
 if(self.stopping||generation!=self.generation||!self.searching)return;
 if(![self sameCandidate:device]){[self trace:@"Ignored SDP callback from a different device address."];return;}
 [self trace:[NSString stringWithFormat:@"SDP completed: status=0x%08x, services=%lu",(unsigned)status,(unsigned long)device.services.count]];
 if(status!=kIOReturnSuccess){[self finishWithError:[NSString stringWithFormat:@"Service discovery failed (0x%08x). Restart Windows Secondary and reconnect.",(unsigned)status]];return;}
 IOBluetoothSDPServiceRecord *record=[device getServiceRecordForUUID:[MBBluetoothLink serviceUUID]];
 if(!record)record=[self.candidate getServiceRecordForUUID:[MBBluetoothLink serviceUUID]];
 if(!record){[self finishWithError:@"No MouseBridge service found. Choose Secondary PC in the Windows app before connecting."];return;}
 [self openService:record device:device];
}
- (void)openService:(IOBluetoothSDPServiceRecord *)record device:(IOBluetoothDevice *)device {
 if(!self.searching||self.stopping)return;BluetoothRFCOMMChannelID cid=0;IOReturn result=[record getRFCOMMChannelID:&cid];
 if(result!=kIOReturnSuccess||cid==0){[self finishWithError:@"MouseBridge service has no valid RFCOMM channel."];return;}
 self.searching=NO;self.opening=YES;self.phaseStarted=NSProcessInfo.processInfo.systemUptime;
 [self report:[NSString stringWithFormat:@"Connecting to MouseBridge · RFCOMM channel %u…",(unsigned)cid]];
 IOBluetoothRFCOMMChannel *opened=nil;IOReturn err=[device openRFCOMMChannelAsync:&opened withChannelID:cid delegate:self];
 [self trace:[NSString stringWithFormat:@"openRFCOMMChannelAsync returned 0x%08x; channel object=%@",(unsigned)err,opened?@"present":@"absent"]];
 if(self.stopping)return;self.channel=opened;
 if(err!=kIOReturnSuccess||!opened)[self finishWithError:[NSString stringWithFormat:@"RFCOMM connection failed (0x%08x).",(unsigned)err]];
}
- (void)opened:(IOBluetoothUserNotification *)notification channel:(IOBluetoothRFCOMMChannel *)channel generation:(NSUInteger)generation {
 // Never close a channel belonging to another app, an old registration or a different service.
 if(self.stopping||!self.listening||generation!=self.generation)return;
 if([channel getChannelID]!=self.serverChannel){[self trace:@"RFCOMM open notification ignored: channel does not match this service (no peer details logged)."];return;}
 [self trace:[NSString stringWithFormat:@"RFCOMM open notification: matching channel=%u; incoming=%@; mainThread=%@",(unsigned)self.serverChannel,[channel isIncoming]?@"YES":@"NO",NSThread.isMainThread?@"YES":@"NO"]];
 if(![channel isIncoming]){[self trace:@"Ignoring outgoing channel notification."];return;}
 if(channel==self.channel){[self trace:@"Duplicate notification for active channel ignored."];return;}
 if(self.channel){[self trace:@"Rejecting an additional incoming connection: already busy."];[channel closeChannel];return;}
 self.channel=channel;self.receiveReady=NO;
 IOReturn result=[channel setDelegate:self];[self trace:[NSString stringWithFormat:@"Incoming channel delegate registration returned 0x%08x",(unsigned)result]];
 if(result!=kIOReturnSuccess){[self finishWithError:@"Could not register incoming RFCOMM delegate."];return;}
 [self acceptChannel];
}
- (void)rfcommChannelOpenComplete:(IOBluetoothRFCOMMChannel *)channel status:(IOReturn)error {
 if(!NSThread.isMainThread){dispatch_async(dispatch_get_main_queue(),^{[self rfcommChannelOpenComplete:channel status:error];});return;}
 if(self.stopping||!self.opening||![self sameCandidate:[channel getDevice]])return;
 self.channel=channel;if(error!=kIOReturnSuccess){[self finishWithError:[NSString stringWithFormat:@"RFCOMM open failed (0x%08x).",(unsigned)error]];return;}[self acceptChannel];
}
- (void)acceptChannel {
 if(self.stopping||self.attached)return;
 self.baseband=NO;self.opening=NO;self.searching=NO;[self.connectionTimer invalidate];self.connectionTimer=nil;
 IOBluetoothDevice *device=[self.channel getDevice];
 [self trace:[NSString stringWithFormat:@"RFCOMM opened; paired=%@; checking authentication/encryption…",[device isPaired]?@"YES":@"NO"]];
 BOOL paired=[device isPaired];IOReturn authentication=paired?[device requestAuthentication]:kIOReturnNotPermitted;
 BluetoothHCIEncryptionMode encryption=[device getEncryptionMode];
 [self trace:[NSString stringWithFormat:@"RFCOMM security: paired=%@; authentication=0x%08x; encryptionMode=%u",paired?@"YES":@"NO",(unsigned)authentication,(unsigned)encryption]];
 if(!paired||authentication!=kIOReturnSuccess||encryption==0){[self report:@"Connection rejected: paired, authenticated and encrypted Bluetooth is required."];[self stop];return;}
 self.attached=YES;self.receiveReady=NO;[self report:@"Encrypted Bluetooth connected"];
 if(self.connected)self.connected();
}
- (BOOL)secure {return self.ready&&[[self.channel getDevice] isPaired]&&[[self.channel getDevice] getEncryptionMode]!=0;}
- (BOOL)send:(uint8_t)kind payload:(NSData *)payload {
 if(!self.ready||!mb_known_kind(kind)||payload.length>MB_MAX_PAYLOAD)return NO;
 if(self.outgoing.count>=128){[self report:@"Send queue full; connection stopped for safety."];[self stop];return NO;}
 uint32_t n=(uint32_t)payload.length+1;uint8_t header[]={n>>24,n>>16,n>>8,n,kind};NSMutableData *frame=[NSMutableData dataWithBytes:header length:5];[frame appendData:payload];[self.outgoing addObject:frame];[self pump];return YES;
}
- (void)pump {
 if(self.writing||!self.ready||self.outgoing.count==0)return;
 self.inflight=self.outgoing.firstObject;NSUInteger mtu=MAX((NSUInteger)[self.channel getMTU],1);self.writeLength=MIN(mtu,self.inflight.length-self.offset);self.writing=YES;
 IOReturn err=[self.channel writeAsync:(void *)((const uint8_t *)self.inflight.bytes+self.offset) length:(UInt16)self.writeLength refcon:NULL];
 if(err!=kIOReturnSuccess){[self report:@"Bluetooth send failed."];[self stop];}
}
- (void)rfcommChannelWriteComplete:(IOBluetoothRFCOMMChannel *)channel refcon:(void *)refcon status:(IOReturn)error {
 if(!NSThread.isMainThread){dispatch_async(dispatch_get_main_queue(),^{[self rfcommChannelWriteComplete:channel refcon:refcon status:error];});return;}
 if(channel!=self.channel)return;self.writing=NO;
 if(error!=kIOReturnSuccess){[self report:@"Bluetooth send failed."];[self stop];return;}
 self.offset+=self.writeLength;if(self.offset==self.inflight.length){[self.outgoing removeObjectAtIndex:0];self.offset=0;self.inflight=nil;}[self pump];
}
- (void)rfcommChannelData:(IOBluetoothRFCOMMChannel *)channel data:(void *)bytes length:(size_t)length {
 if(!NSThread.isMainThread){
  if(length>MB_MAX_PAYLOAD+5){dispatch_async(dispatch_get_main_queue(),^{if(channel==self.channel)[self finishWithError:@"Receive buffer limit exceeded."];});return;}
  NSData *copy=[NSData dataWithBytes:bytes length:length];
  dispatch_async(dispatch_get_main_queue(),^{[self rfcommChannelData:channel data:(void *)copy.bytes length:copy.length];});return;
 }
 if(channel!=self.channel)return;
 if(length>MB_MAX_PAYLOAD+5||self.received.length+length>2*(MB_MAX_PAYLOAD+5)){[self report:@"Receive buffer limit exceeded."];[self stop];return;}
 [self.received appendBytes:bytes length:length];
 while(self.received.length>=4){const uint8_t *p=self.received.bytes;uint32_t n=mb_body_length(p);if(!mb_valid_body(n)){[self report:@"Invalid frame size."];[self stop];return;}
 if(self.received.length<n+4)return;if(!mb_known_kind(p[4])){[self report:@"Unknown protocol message."];[self stop];return;}
 uint8_t kind=p[4];NSData *payload=[self.received subdataWithRange:NSMakeRange(5,n-1)];[self.received replaceBytesInRange:NSMakeRange(0,n+4) withBytes:NULL length:0];if(!self.receiveReady){
  if(self.pendingIncoming.count>=128||self.pendingBytes+payload.length>512*1024){[self finishWithError:@"Incoming traffic exceeded pending-approval buffer. Reconnect after reviewing the Mac approval dialog."];return;}
  [self.pendingIncoming addObject:@{@"kind":@(kind),@"payload":payload}];self.pendingBytes+=payload.length;
 }else if(self.message)self.message(kind,payload);
 if(!self.ready)return;}
}
- (void)startReceiving {
 if(self.stopping||!self.attached||!self.ready||self.receiveReady)return;
 self.receiveReady=YES;NSArray *queued=self.pendingIncoming.copy;[self.pendingIncoming removeAllObjects];self.pendingBytes=0;
 NSUInteger generation=self.generation;[self trace:[NSString stringWithFormat:@"Application input engine ready; releasing %lu buffered messages",(unsigned long)queued.count]];
 for(NSDictionary *message in queued){if(self.stopping||generation!=self.generation||!self.ready)break;if(self.message)self.message([message[@"kind"] unsignedCharValue],message[@"payload"]);}
}
- (void)rfcommChannelClosed:(IOBluetoothRFCOMMChannel *)channel {
 if(!NSThread.isMainThread){dispatch_async(dispatch_get_main_queue(),^{[self rfcommChannelClosed:channel];});return;}if(channel==self.channel){[self report:@"Peer disconnected"];[self stop];}}
- (void)stop {
 self.generation++;self.stopping=YES;self.listening=self.receiveReady=NO;self.serverChannel=0;self.incoming=nil;[self.pendingIncoming removeAllObjects];self.pendingBytes=0;self.baseband=self.searching=self.opening=self.attached=NO;[self.connectionTimer invalidate];self.connectionTimer=nil;self.query=nil;IOBluetoothRFCOMMChannel *old=self.channel;self.channel=nil;[old setDelegate:nil];[old closeChannel];[self.notification unregister];self.notification=nil;[self.record removeServiceRecord];self.record=nil;self.candidate=nil;[self.received setLength:0];[self.outgoing removeAllObjects];self.inflight=nil;self.offset=0;self.writing=NO;if(self.closed)self.closed();
}
@end
