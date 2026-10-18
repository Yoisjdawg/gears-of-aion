// Daevas of E-Day: C export for the ooz Kraken/Mermaid/Selkie/Leviathan decompressor
// (ooz by Powzix, GPL-3.0-or-later, https://github.com/powzix/ooz).
// Used to read the player's own Gears of War: E-Day IoStore archives.
#include <stddef.h>
#include <stdlib.h>
#include <string.h>

typedef unsigned char byte;
int Kraken_Decompress(const byte *src, size_t src_len, byte *dst, size_t dst_len);

extern "C" __declspec(dllexport) int OozDecompress(const byte *src, int src_len, byte *dst, int dst_len) {
  // ooz writes up to 64 bytes past the end of its output, so decode into a padded buffer.
  byte *tmp = (byte *)malloc((size_t)dst_len + 64);
  if (!tmp) return -1;
  int n = Kraken_Decompress(src, (size_t)src_len, tmp, (size_t)dst_len);
  if (n == dst_len) memcpy(dst, tmp, (size_t)dst_len);
  free(tmp);
  return n == dst_len ? n : -1;
}
