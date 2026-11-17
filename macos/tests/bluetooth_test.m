#import <Foundation/Foundation.h>
#import "../MouseBridge/BluetoothLink.h"
#include <assert.h>
@interface MBFakeRecord:NSObject
@end
@implementation MBFakeRecord
- (IOReturn)getRFCOMMChannelID:(BluetoothRFCOMMChannelID *)cid{*cid=7;return kIOReturnSuccess;}
@end
@interface MBFakeChannel:NSObject
@end
@implementation MBFakeChannel
- (IOReturn)setDelegate:(id)delegate{return kIOReturnSuccess;}
- (IOReturn)closeChannel{return kIOReturnSuccess;}
- (BOOL)isOpen{return NO;}
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
@end
@interface MBTestLink:MBBluetoothLink
@end
@implementation MBTestLink
- (void)traceControllerState {} // No live controller/permission access in mocked-device tests.
@end
static MBFakeDevice *device(void){MBFakeDevice *d=[MBFakeDevice new];d.connected=YES;d.addressString=@"00-11-22-33-44-AA";return d;}
static void spin(double seconds){[[NSRunLoop mainRunLoop]runUntilDate:[NSDate dateWithTimeIntervalSinceNow:seconds]];}
int main(void){@autoreleasepool{
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
 // Polling also recovers when only the connected flag changes and the ACL callback is missing.
 d=device();d.connected=NO;[link connect:(id)d];d.connected=YES;spin(.26);assert(d.target);[link stop];
 puts("PASS: authenticated baseband before SDP, ACL timeout/error, connection-exists, stale ACL callback and connected-flag polling (no radio)");
 puts("PASS: SDP callback timeout, address-wrapper identity, stale generation, channel-open timeout and immediate error recovery (no radio)");
}return 0;}
