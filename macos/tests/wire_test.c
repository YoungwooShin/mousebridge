#include "../MouseBridge/Wire.h"
#include <assert.h>
#include <stdio.h>
int main(void){
 uint8_t bytes[4];mb_write_le(bytes,0x87654321);assert(mb_read_le(bytes)==0x87654321);assert(mb_body_length((uint8_t[]){0,1,0,1})==65537);
 assert(mb_valid_body(1)&&mb_valid_body(65537));assert(!mb_valid_body(0)&&!mb_valid_body(65538));
 for(int i=0;i<256;i++){bool expected=(i>=1&&i<=4)||(i>=10&&i<=17)||(i>=20&&i<=26);assert(mb_known_kind(i)==expected);}
 assert(mb_windows_key(55)==0xa2&&mb_mac_key(0xa2)==55);assert(mb_windows_key(56)==0xa0&&mb_windows_key(60)==0xa1);assert(mb_mac_key(0x77)==100);
 for(int k=0;k<128;k++){int vk=mb_windows_key(k);if(vk)assert(mb_mac_key(vk)>=0);}assert(mb_windows_key(999)==0&&mb_mac_key(255)==-1);
 assert(mb_valid_action(1,255,255,3));assert(!mb_valid_action(1,256,0,0));assert(!mb_valid_action(1,65,0,4));
 assert(mb_valid_action(2,-32767,32767,0));assert(!mb_valid_action(2,-32768,0,0));assert(!mb_valid_action(2,0,0,1));
 assert(mb_valid_action(3,5,1,0));assert(!mb_valid_action(3,6,1,0));assert(!mb_valid_action(3,1,2,0));
 assert(mb_valid_action(4,120,1,0));assert(!mb_valid_action(4,0,2,0));assert(!mb_valid_action(5,0,0,0));
 puts("PASS: Mac wire endianness, all frame kinds, bounds, key mapping and action validation");return 0;}
