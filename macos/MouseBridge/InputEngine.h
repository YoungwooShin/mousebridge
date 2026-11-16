#import <Cocoa/Cocoa.h>
#import "BluetoothLink.h"
@interface MBInputEngine : NSObject
@property(copy) void(^state)(NSString *);
@property BOOL secondaryLeft,edgeEnabled;
- (instancetype)initWithLink:(MBBluetoothLink *)link secondary:(BOOL)secondary;
- (BOOL)start;
- (void)toggle;
- (void)handle:(uint8_t)kind payload:(NSData *)payload;
- (void)stop;
@end
