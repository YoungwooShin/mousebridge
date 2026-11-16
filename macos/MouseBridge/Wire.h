#ifndef MB_WIRE_H
#define MB_WIRE_H
#include <stdint.h>
#include <stddef.h>
#include <stdbool.h>
#define MB_MAX_PAYLOAD 65536
#define MB_MAX_CLIPBOARD 262144
#define MB_PROTOCOL_VERSION 3
static inline uint32_t mb_read_le(const uint8_t *p){return (uint32_t)p[0]|(uint32_t)p[1]<<8|(uint32_t)p[2]<<16|(uint32_t)p[3]<<24;}
static inline void mb_write_le(uint8_t *p,uint32_t n){for(int i=0;i<4;i++)p[i]=(uint8_t)(n>>(i*8));}
static inline uint32_t mb_body_length(const uint8_t *p){return (uint32_t)p[0]<<24|(uint32_t)p[1]<<16|(uint32_t)p[2]<<8|p[3];}
static inline bool mb_known_kind(uint8_t k){return (k>=1&&k<=4)||(k>=10&&k<=17)||(k>=20&&k<=26);}
static inline bool mb_valid_body(uint32_t n){return n>=1&&n<=MB_MAX_PAYLOAD+1;}
static inline bool mb_valid_action(uint8_t kind,int32_t a,int32_t b,int32_t c){
 switch(kind){case 1:return a>=1&&a<=255&&b>=0&&b<=255&&c>=0&&c<=3;
 case 2:return a>=-32767&&a<=32767&&b>=-32767&&b<=32767&&c==0;
 case 3:return a>=1&&a<=5&&(b==0||b==1)&&c==0;
 case 4:return a>=-32767&&a<=32767&&(b==0||b==1)&&c==0;default:return false;}}
// Mac virtual keycode -> Windows VK. Command and Control exchange semantics so normal Copy/Paste travels correctly.
static inline int mb_windows_key(unsigned key){
 static const int table[128]={
 [0]=0x41,[1]=0x53,[2]=0x44,[3]=0x46,[4]=0x48,[5]=0x47,[6]=0x5a,[7]=0x58,[8]=0x43,[9]=0x56,[11]=0x42,
 [12]=0x51,[13]=0x57,[14]=0x45,[15]=0x52,[16]=0x59,[17]=0x54,[18]=0x31,[19]=0x32,[20]=0x33,[21]=0x34,[22]=0x36,[23]=0x35,
 [24]=0xbb,[25]=0x39,[26]=0x37,[27]=0xbd,[28]=0x38,[29]=0x30,[30]=0xdd,[31]=0x4f,[32]=0x55,[33]=0xdb,[34]=0x49,[35]=0x50,
 [36]=0x0d,[37]=0x4c,[38]=0x4a,[39]=0xde,[40]=0x4b,[41]=0xba,[42]=0xdc,[43]=0xbc,[44]=0xbf,[45]=0x4e,[46]=0x4d,[47]=0xbe,[48]=0x09,[49]=0x20,[50]=0xc0,[51]=0x08,[53]=0x1b,
 [54]=0xa3,[55]=0xa2,[56]=0xa0,[57]=0x14,[58]=0xa4,[59]=0x5b,[60]=0xa1,[61]=0xa5,[62]=0x5c,
 [64]=0x80,[65]=0x6e,[67]=0x6a,[69]=0x6b,[71]=0x90,[75]=0x6f,[76]=0x0d,[78]=0x6d,[79]=0x81,[80]=0x82,[81]=0xbb,
 [82]=0x60,[83]=0x61,[84]=0x62,[85]=0x63,[86]=0x64,[87]=0x65,[88]=0x66,[89]=0x67,[91]=0x68,[92]=0x69,
 [96]=0x74,[97]=0x75,[98]=0x76,[99]=0x72,[100]=0x77,[101]=0x78,[103]=0x7a,[105]=0x7c,[106]=0x7f,[107]=0x7d,[109]=0x79,[111]=0x7b,[113]=0x7e,[114]=0x2d,[115]=0x24,[116]=0x21,[117]=0x2e,[118]=0x73,[119]=0x23,[120]=0x71,[121]=0x22,[122]=0x70,[123]=0x25,[124]=0x27,[125]=0x28,[126]=0x26};
 return key<128?table[key]:0;}
static inline int mb_mac_key(int vk){if(vk==0x10)vk=0xa0;if(vk==0x11)vk=0xa2;if(vk==0x12)vk=0xa4;for(unsigned i=0;i<128;i++)if(mb_windows_key(i)==vk)return (int)i;return -1;}
#endif
