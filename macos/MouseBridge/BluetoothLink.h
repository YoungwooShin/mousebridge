#import <Foundation/Foundation.h>
#import <CoreBluetooth/CoreBluetooth.h>
#pragma clang diagnostic push
#pragma clang diagnostic ignored "-Wnullability-completeness"
#import <IOBluetooth/IOBluetooth.h>
#pragma clang diagnostic pop
@interface MBBluetoothLink : NSObject <IOBluetoothRFCOMMChannelDelegate>
@property(copy) void(^message)(uint8_t,NSData *);
@property(copy) void(^state)(NSString *);
@property(copy) void(^diagnostic)(NSString *);
- (instancetype)initWithDiscoveryTimeout:(NSTimeInterval)discoveryTimeout openTimeout:(NSTimeInterval)openTimeout;
@property(copy) void(^connected)(void);
@property(copy) void(^closed)(void);
@property(readonly) BOOL ready;
@property(readonly) NSString *peerName;
+ (NSArray<IOBluetoothDevice *> *)pairedDevices;
- (void)listen;
- (void)connect:(IOBluetoothDevice *)device;
- (BOOL)send:(uint8_t)kind payload:(NSData *)payload;
- (BOOL)secure;
- (void)stop;
@end
