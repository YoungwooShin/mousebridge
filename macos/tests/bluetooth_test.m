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
@property IOReturn queryError;
@property NSUInteger opens;
@end
@implementation MBFakeDevice
- (BOOL)isPaired{return YES;}
- (NSArray *)services{return self.record?@[self.record]:@[];}
- (IOReturn)performSDPQuery:(id)target{self.target=target;return self.queryError;}
- (id)getServiceRecordForUUID:(IOBluetoothSDPUUID *)uuid{return self.record;}
- (IOReturn)openRFCOMMChannelAsync:(IOBluetoothRFCOMMChannel **)out withChannelID:(BluetoothRFCOMMChannelID)cid delegate:(id)delegate{assert(cid==7);self.opens++;*out=(id)[MBFakeChannel new];return kIOReturnSuccess;}
@end
@interface NSObject(MBTestCallback)
- (void)sdpQueryComplete:(IOBluetoothDevice *)device status:(IOReturn)status;
@end
static MBFakeDevice *device(void){MBFakeDevice *d=[MBFakeDevice new];d.addressString=@"00-11-22-33-44-AA";return d;}
static void spin(double seconds){[[NSRunLoop mainRunLoop]runUntilDate:[NSDate dateWithTimeIntervalSinceNow:seconds]];}
int main(void){@autoreleasepool{
 __block NSString *state=@"";MBBluetoothLink *link=[[MBBluetoothLink alloc]initWithDiscoveryTimeout:.04 openTimeout:.04];link.state=^(NSString *s){state=s;};
 MBFakeDevice *d=device();[link connect:(id)d];assert([state containsString:@"Discovering"]);spin(.35);assert([state containsString:@"discovery timed out"]);
 // The callback may supply a different wrapper for the same Bluetooth address.
 [link connect:(id)d];MBFakeDevice *wrapper=device();wrapper.addressString=@"00-11-22-33-44-aa";[d.target sdpQueryComplete:(id)wrapper status:0];spin(.02);assert([state containsString:@"No MouseBridge service"]);
 // A completed callback belonging to the previous attempt must not affect a reconnect.
 [link connect:(id)d];id old=d.target;[link connect:(id)d];[old sdpQueryComplete:(id)wrapper status:0];spin(.02);assert([state containsString:@"Discovering"]);[link stop];
 // A matching discovered service opens once and the open phase has its own deadline.
 [link connect:(id)d];wrapper.record=[MBFakeRecord new];[d.target sdpQueryComplete:(id)wrapper status:0];spin(.02);assert(wrapper.opens==1);assert([state containsString:@"RFCOMM channel 7"]);spin(.35);assert([state containsString:@"connection timed out"]);
 d.queryError=kIOReturnError;[link connect:(id)d];assert([state containsString:@"could not start"]);[link stop];
 puts("PASS: SDP callback timeout, address-wrapper identity, stale generation, channel-open timeout and immediate error recovery (no radio)");
}return 0;}
