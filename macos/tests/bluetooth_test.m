#import <Foundation/Foundation.h>
#import "../MouseBridge/BluetoothLink.h"
#include <assert.h>
@interface MBFakeRecord:NSObject
@end
@implementation MBFakeRecord
- (IOReturn)getRFCOMMChannelID:(BluetoothRFCOMMChannelID *)cid{*cid=7;return kIOReturnSuccess;}
- (IOReturn)removeServiceRecord{return kIOReturnSuccess;}
@end
@interface MBFakeChannel:NSObject
@property BOOL incoming,openStatus;
@property BluetoothRFCOMMChannelID channelID;
@property id peer;
@property(weak) id delegate;
@property NSUInteger closes,delegateCalls;
@end
@implementation MBFakeChannel
- (IOReturn)setDelegate:(id)delegate{self.delegate=delegate;self.delegateCalls++;return kIOReturnSuccess;}
- (IOReturn)closeChannel{self.closes++;self.openStatus=NO;return kIOReturnSuccess;}
- (BOOL)isOpen{return self.openStatus;}
- (BOOL)isIncoming{return self.incoming;}
- (BluetoothRFCOMMChannelID)getChannelID{return self.channelID;}
- (IOBluetoothDevice *)getDevice{return self.peer;}
@end
@interface MBFakeDevice:NSObject
@property NSString *addressString;
@property id target,record;
@property IOReturn queryError,openError;
@property BOOL connected,connectDuringOpen;
@property id connectionTarget;
@property NSUInteger basebandOpens;
@property NSUInteger opens;
@end
@implementation MBFakeDevice
- (BOOL)isPaired{return YES;}
- (IOReturn)requestAuthentication{return kIOReturnSuccess;}
- (BluetoothHCIEncryptionMode)getEncryptionMode{return 1;}
- (BOOL)isConnected{return self.connected;}
- (IOReturn)openConnection:(id)target withPageTimeout:(BluetoothHCIPageTimeout)timeout authenticationRequired:(BOOL)auth {
 assert(auth==YES);self.basebandOpens++;self.connectionTarget=target;if(self.connectDuringOpen)self.connected=YES;return self.openError;
}
- (NSArray *)services{return self.record?@[self.record]:@[];}
- (IOReturn)performSDPQuery:(id)target{self.target=target;return self.queryError;}
- (id)getServiceRecordForUUID:(IOBluetoothSDPUUID *)uuid{return self.record;}
- (IOReturn)openRFCOMMChannelAsync:(IOBluetoothRFCOMMChannel **)out withChannelID:(BluetoothRFCOMMChannelID)cid delegate:(id)delegate{assert(cid==7);self.opens++;*out=(id)[MBFakeChannel new];return kIOReturnSuccess;}
@end
@interface NSObject(MBTestCallback)
- (void)sdpQueryComplete:(IOBluetoothDevice *)device status:(IOReturn)status;
- (void)connectionComplete:(IOBluetoothDevice *)device status:(IOReturn)status;
- (void)opened:(IOBluetoothUserNotification *)notification channel:(IOBluetoothRFCOMMChannel *)channel;
@end
@interface MBBluetoothLink(SDPTest)
+ (NSDictionary *)serviceDictionary;
@end
@interface MBFakeNotification:NSObject
@property BOOL unregistered;
@end
@implementation MBFakeNotification
- (void)unregister{self.unregistered=YES;}
@end
@interface MBTestLink:MBBluetoothLink
@property id incomingTarget;
@property MBFakeNotification *testNotification;
@end
@implementation MBTestLink
- (IOBluetoothSDPServiceRecord *)publishRecord:(NSDictionary *)dictionary{return (id)[MBFakeRecord new];}
- (IOBluetoothUserNotification *)registerIncoming:(id)target{self.incomingTarget=target;self.testNotification=[MBFakeNotification new];return (id)self.testNotification;}
- (void)traceControllerState {} // No live controller/permission access in mocked-device tests.
@end
static MBFakeDevice *device(void){MBFakeDevice *d=[MBFakeDevice new];d.connected=YES;d.addressString=@"00-11-22-33-44-AA";return d;}
static void spin(double seconds){[[NSRunLoop mainRunLoop]runUntilDate:[NSDate dateWithTimeIntervalSinceNow:seconds]];}
int main(void){@autoreleasepool{
 // Exercise Apple's actual SDP value parser, without publishing or using a Bluetooth controller.
 IOBluetoothSDPDataElement *legacy=[IOBluetoothSDPDataElement withElementValue:@3];assert(legacy.getSize==4);
 NSDictionary *record=[MBBluetoothLink serviceDictionary];
 IOBluetoothSDPDataElement *protocols=[IOBluetoothSDPDataElement withElementValue:record[@"0004 - ProtocolDescriptorList"]];
 NSArray *list=protocols.getArrayValue;IOBluetoothSDPDataElement *rfcomm=list[1];IOBluetoothSDPDataElement *parameter=rfcomm.getArrayValue[1];
 assert(parameter.getTypeDescriptor==1);assert(parameter.getSize==1);assert(parameter.getNumberValue.unsignedIntValue==3);
 IOBluetoothSDPDataElement *classes=[IOBluetoothSDPDataElement withElementValue:record[@"0001 - ServiceClassIDList"]];assert(classes.getArrayValue.count==1);
 assert(record[@"0005 - BrowseGroupList"]!=nil);
 puts("PASS: actual Apple SDP parser confirms RFCOMM uint8 channel (legacy NSNumber was uint32) and browse/class attributes");
 __block NSString *state=@"";MBBluetoothLink *link=[[MBTestLink alloc]initWithDiscoveryTimeout:.04 openTimeout:.04];link.state=^(NSString *s){state=s;};
 MBFakeDevice *d=device();[link connect:(id)d];assert([state containsString:@"Discovering"]);spin(.35);assert([state containsString:@"discovery timed out"]);
 // The callback may supply a different wrapper for the same Bluetooth address.
 [link connect:(id)d];MBFakeDevice *wrapper=device();wrapper.addressString=@"00-11-22-33-44-aa";[d.target sdpQueryComplete:(id)wrapper status:0];spin(.02);assert([state containsString:@"No MouseBridge service"]);
 // A completed callback belonging to the previous attempt must not affect a reconnect.
 [link connect:(id)d];id old=d.target;[link connect:(id)d];[old sdpQueryComplete:(id)wrapper status:0];spin(.02);assert([state containsString:@"Discovering"]);[link stop];
 // A matching discovered service opens once and the open phase has its own deadline.
 [link connect:(id)d];wrapper.record=[MBFakeRecord new];[d.target sdpQueryComplete:(id)wrapper status:0];spin(.02);assert(wrapper.opens==1);assert([state containsString:@"RFCOMM channel 7"]);spin(.35);assert([state containsString:@"connection timed out"]);
 d.queryError=kIOReturnError;[link connect:(id)d];assert([state containsString:@"could not start"]);[link stop];
 // Establish an authenticated ACL before SDP when the device is not connected.
 d=device();d.connected=NO;[link connect:(id)d];assert(d.basebandOpens==1);assert(!d.target);assert([state containsString:@"Opening authenticated"]);
 d.connected=YES;[d.connectionTarget connectionComplete:(id)d status:0];spin(.02);assert(d.target);assert([state containsString:@"Discovering"]);[link stop];
 // An ACL timeout is distinct from a missing SDP callback; SDP must never have started.
 d=device();d.connected=NO;[link connect:(id)d];spin(.35);assert([state containsString:@"baseband connection timed out"]);assert(!d.target);
 // Immediate ACL errors preserve their phase and native error code.
 d=device();d.connected=NO;d.openError=kIOReturnError;[link connect:(id)d];assert([state containsString:@"baseband connection could not start"]);assert(!d.target);
 // The OS may return connection-exists while the connected flag has become true.
 d=device();d.connected=NO;d.connectDuringOpen=YES;d.openError=kIOReturnError;[link connect:(id)d];assert([state containsString:@"Discovering"]);assert(d.target);[link stop];
 // A stale ACL callback must not advance a newer attempt.
 d=device();d.connected=NO;[link connect:(id)d];old=d.connectionTarget;[link connect:(id)d];[old connectionComplete:(id)d status:0];spin(.02);assert([state containsString:@"Opening authenticated"]);assert(!d.target);[link stop];
 // A success callback with connected=NO must not be mistaken for an established transport.
 d=device();d.connected=NO;[link connect:(id)d];[d.connectionTarget connectionComplete:(id)d status:0];spin(.02);
 assert([state containsString:@"Waiting for Bluetooth connection confirmation"]);assert(!d.target);
 spin(.35);assert([state containsString:@"baseband connection timed out"]);assert(!d.target);
 // Polling also recovers when only the connected flag changes and the ACL callback is missing.
 d=device();d.connected=NO;[link connect:(id)d];d.connected=YES;spin(.26);assert(d.target);[link stop];
 // Incoming notifications: never take over or close another service/outgoing/stale channel.
 MBTestLink *server=[[MBTestLink alloc]initWithDiscoveryTimeout:.04 openTimeout:.04];
 __block int connects=0;NSMutableArray<NSData *> *messages=[NSMutableArray array];__block NSString *serverState=@"";
 server.connected=^{connects++;};server.message=^(uint8_t kind,NSData *p){assert(kind==1);[messages addObject:p];};server.state=^(NSString *v){serverState=v;};
 [server listen];id firstObserver=server.incomingTarget;MBFakeNotification *firstNotification=server.testNotification;
 MBFakeChannel *unrelated=[MBFakeChannel new];unrelated.channelID=8;unrelated.incoming=YES;
 [firstObserver opened:(id)firstNotification channel:(id)unrelated];assert(unrelated.closes==0&&unrelated.delegateCalls==0);
 MBFakeChannel *outgoing=[MBFakeChannel new];outgoing.channelID=7;outgoing.incoming=NO;
 [firstObserver opened:(id)firstNotification channel:(id)outgoing];assert(outgoing.closes==0&&outgoing.delegateCalls==0);
 MBFakeChannel *accepted=[MBFakeChannel new];accepted.channelID=7;accepted.incoming=YES;accepted.openStatus=YES;accepted.peer=device();
 [firstObserver opened:(id)firstNotification channel:(id)accepted];assert(connects==1);assert(accepted.closes==0&&accepted.delegateCalls==1);
 // Windows may send protocol frames while the Mac is still waiting for user approval.
 uint8_t frameA[]={0,0,0,2,1,'a'},frameB[]={0,0,0,2,1,'b'};
 [server rfcommChannelData:(id)accepted data:frameA length:sizeof(frameA)];[server rfcommChannelData:(id)accepted data:frameB length:sizeof(frameB)];assert(messages.count==0);
 [server startReceiving];assert(messages.count==2);assert(((const uint8_t *)messages[0].bytes)[0]=='a');assert(((const uint8_t *)messages[1].bytes)[0]=='b');
 [server startReceiving];assert(messages.count==2);
 [firstObserver opened:(id)firstNotification channel:(id)accepted];assert(connects==1&&accepted.closes==0&&accepted.delegateCalls==1);
 MBFakeChannel *extra=[MBFakeChannel new];extra.channelID=7;extra.incoming=YES;extra.openStatus=YES;
 [firstObserver opened:(id)firstNotification channel:(id)extra];assert(extra.closes==1&&accepted.closes==0);
 [server stop];assert(firstNotification.unregistered);[server listen];
 MBFakeChannel *stale=[MBFakeChannel new];stale.channelID=7;stale.incoming=YES;
 [firstObserver opened:(id)firstNotification channel:(id)stale];assert(stale.closes==0&&stale.delegateCalls==0);
 MBFakeChannel *pending=[MBFakeChannel new];pending.channelID=7;pending.incoming=YES;pending.openStatus=YES;pending.peer=device();
 [server.incomingTarget opened:(id)server.testNotification channel:(id)pending];
 for(int i=0;i<129;i++)[server rfcommChannelData:(id)pending data:frameA length:sizeof(frameA)];
 assert([serverState containsString:@"pending-approval buffer"]);assert(pending.closes==1);assert(messages.count==2);[server stop];
 // Driver callbacks on a background thread must reach AppKit/application callbacks on main.
 MBTestLink *threaded=[[MBTestLink alloc]initWithDiscoveryTimeout:.04 openTimeout:.04];__block int threadConnects=0,threadMessages=0;
 threaded.connected=^{assert(NSThread.isMainThread);threadConnects++;[threaded startReceiving];};
 threaded.message=^(uint8_t kind,NSData *payload){assert(NSThread.isMainThread);assert(kind==1);threadMessages++;};
 [threaded listen];id observer=threaded.incomingTarget;id note=threaded.testNotification;
 MBFakeChannel *threadChannel=[MBFakeChannel new];threadChannel.channelID=7;threadChannel.incoming=YES;threadChannel.openStatus=YES;threadChannel.peer=device();
 NSData *threadFrame=[NSData dataWithBytes:frameA length:sizeof(frameA)];
 dispatch_async(dispatch_get_global_queue(QOS_CLASS_DEFAULT,0),^{[observer opened:note channel:(id)threadChannel];[threaded rfcommChannelData:(id)threadChannel data:(void *)threadFrame.bytes length:threadFrame.length];});
 spin(.35);assert(threadConnects==1&&threadMessages==1);[threaded stop];threaded.connected=nil;
 puts("PASS: background incoming/data callbacks normalized to main before application/UI use (no radio)");
 puts("PASS: incoming channel/direction/generation guards, duplicate safety, busy rejection, approval-buffer ordering/once-only delivery and bounded overflow (no radio)");
 puts("PASS: authenticated baseband before SDP, ACL timeout/error, connection-exists, stale ACL callback, success-with-disconnected rejection and connected-flag polling (no radio)");
 puts("PASS: SDP callback timeout, address-wrapper identity, stale generation, channel-open timeout and immediate error recovery (no radio)");
}return 0;}
