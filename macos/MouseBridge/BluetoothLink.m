#import "BluetoothLink.h"
#import "Wire.h"
@interface MBBluetoothLink ()
@property IOBluetoothRFCOMMChannel *channel;
@property IOBluetoothSDPServiceRecord *record;
@property IOBluetoothUserNotification *notification;
@property IOBluetoothDevice *candidate;
@property NSMutableData *received;
@property NSMutableArray<NSData *> *outgoing;
@property NSData *inflight;
@property NSUInteger offset,writeLength;
@property BOOL writing,stopping;
@end
@implementation MBBluetoothLink
+ (IOBluetoothSDPUUID *)serviceUUID {const unsigned char bytes[]={0x9d,0x96,0xd1,0xbd,0x27,0x86,0x4b,0x5d,0x97,0xe7,0xcf,0x84,0x39,0x48,0x43,0xaf};return [IOBluetoothSDPUUID uuidWithBytes:bytes length:16];}
+ (NSArray *)pairedDevices {return [IOBluetoothDevice pairedDevices]?:@[];}
- (instancetype)init {if((self=[super init])){_received=[NSMutableData data];_outgoing=[NSMutableArray array];}return self;}
- (NSString *)peerName{return [[self.channel getDevice] nameOrAddress]?:@"Paired computer";}
- (BOOL)ready{return self.channel!=nil&&[self.channel isOpen];}
- (void)report:(NSString *)text{if(self.state)self.state(text);}
- (void)listen {
 [self stop];self.stopping=NO;
 NSDictionary *dict=@{@"0001 - ServiceClassIDList":@[[MBBluetoothLink serviceUUID]],@"0004 - ProtocolDescriptorList":@[@[[IOBluetoothSDPUUID uuid16:0x0100]],@[[IOBluetoothSDPUUID uuid16:0x0003],@3]],@"0100 - ServiceName":@"MouseBridge",@"LocalAttributes":@{@"Persistent":@NO}};
 self.record=[IOBluetoothSDPServiceRecord publishedServiceRecordWithDictionary:dict];BluetoothRFCOMMChannelID channel=0;
 if(!self.record||[self.record getRFCOMMChannelID:&channel]!=kIOReturnSuccess){[self report:@"Cannot advertise RFCOMM. Check Bluetooth permission and radio."];[self stop];return;}
 self.notification=[IOBluetoothRFCOMMChannel registerForChannelOpenNotifications:self selector:@selector(opened:channel:) withChannelID:channel direction:kIOBluetoothUserNotificationChannelDirectionIncoming];
 [self report:@"Secondary ready. Pair in system Bluetooth settings first."];
}
- (void)connect:(IOBluetoothDevice *)device {
 [self stop];self.stopping=NO;if(![device isPaired]){[self report:@"Pair the computers in system Bluetooth settings first."];return;}
 self.candidate=device;[self report:@"Discovering MouseBridge service…"];
 IOReturn err=[device performSDPQuery:self uuids:@[[MBBluetoothLink serviceUUID]]];if(err!=kIOReturnSuccess){[self report:@"Service discovery could not start."];[self stop];}
}
- (void)sdpQueryComplete:(IOBluetoothDevice *)device status:(IOReturn)status {
 if(self.stopping||device!=self.candidate)return;
 IOBluetoothSDPServiceRecord *record=[device getServiceRecordForUUID:[MBBluetoothLink serviceUUID]];BluetoothRFCOMMChannelID cid=0;
 if(status!=kIOReturnSuccess||!record||[record getRFCOMMChannelID:&cid]!=kIOReturnSuccess){[self report:@"No service found. Start Secondary on the other computer, then reconnect."];return;}
 IOBluetoothRFCOMMChannel *opened=nil;IOReturn err=[device openRFCOMMChannelAsync:&opened withChannelID:cid delegate:self];self.channel=opened;
 if(err!=kIOReturnSuccess){[self report:@"RFCOMM connection failed."];[self stop];}
}
- (void)opened:(IOBluetoothUserNotification *)notification channel:(IOBluetoothRFCOMMChannel *)channel {
 if(self.stopping||self.channel){[channel closeChannel];return;}
 self.channel=channel;[channel setDelegate:self];[self acceptChannel];
}
- (void)rfcommChannelOpenComplete:(IOBluetoothRFCOMMChannel *)channel status:(IOReturn)error {
 if(channel!=self.channel)return;if(error!=kIOReturnSuccess){[self report:@"RFCOMM open failed."];[self stop];return;}[self acceptChannel];
}
- (void)acceptChannel {
 IOBluetoothDevice *device=[self.channel getDevice];
 if(![device isPaired]||[device requestAuthentication]!=kIOReturnSuccess||[device getEncryptionMode]==0){[self report:@"Connection rejected: paired, authenticated and encrypted Bluetooth is required."];[self stop];return;}
 [self report:@"Encrypted Bluetooth connected"];
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
 if(channel!=self.channel)return;self.writing=NO;
 if(error!=kIOReturnSuccess){[self report:@"Bluetooth send failed."];[self stop];return;}
 self.offset+=self.writeLength;if(self.offset==self.inflight.length){[self.outgoing removeObjectAtIndex:0];self.offset=0;self.inflight=nil;}[self pump];
}
- (void)rfcommChannelData:(IOBluetoothRFCOMMChannel *)channel data:(void *)bytes length:(size_t)length {
 if(channel!=self.channel)return;
 if(length>MB_MAX_PAYLOAD+5||self.received.length+length>2*(MB_MAX_PAYLOAD+5)){[self report:@"Receive buffer limit exceeded."];[self stop];return;}
 [self.received appendBytes:bytes length:length];
 while(self.received.length>=4){const uint8_t *p=self.received.bytes;uint32_t n=mb_body_length(p);if(!mb_valid_body(n)){[self report:@"Invalid frame size."];[self stop];return;}
 if(self.received.length<n+4)return;if(!mb_known_kind(p[4])){[self report:@"Unknown protocol message."];[self stop];return;}
 uint8_t kind=p[4];NSData *payload=[self.received subdataWithRange:NSMakeRange(5,n-1)];[self.received replaceBytesInRange:NSMakeRange(0,n+4) withBytes:NULL length:0];if(self.message)self.message(kind,payload);if(!self.ready)return;}
}
- (void)rfcommChannelClosed:(IOBluetoothRFCOMMChannel *)channel {if(channel==self.channel){[self report:@"Peer disconnected"];[self stop];}}
- (void)stop {
 self.stopping=YES;IOBluetoothRFCOMMChannel *old=self.channel;self.channel=nil;[old setDelegate:nil];[old closeChannel];[self.notification unregister];self.notification=nil;[self.record removeServiceRecord];self.record=nil;self.candidate=nil;[self.received setLength:0];[self.outgoing removeAllObjects];self.inflight=nil;self.offset=0;self.writing=NO;if(self.closed)self.closed();
}
@end
