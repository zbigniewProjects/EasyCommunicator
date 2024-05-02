using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DNUploader.Extensions
{
    static internal class Extensions
    {
        #region hashes
        public static int CustomStringHash(this string input)
        {
            int hash = 17; // Initial prime number
            foreach (char c in input)
            {
                // Simple bitwise shift and addition operation
                hash = hash * 31 + c;
            }
            return hash;
        }

        public static int GetStableHashCode(this string text)
        {
            unchecked
            {
                uint hash = 0x811c9dc5;
                uint prime = 0x1000193;

                for (int i = 0; i < text.Length; ++i)
                {
                    byte value = (byte)text[i];
                    hash = hash ^ value;
                    hash *= prime;
                }

                //UnityEngine.Debug.Log($"Created stable hash {(ushort)hash} for {text}");
                return (int)hash;
            }
        }

        // smaller version of our GetStableHashCode.
        // careful, this significantly increases chance of collisions.
        public static ushort GetStableHashCode16(this string text)
        {
            // deterministic hash
            int hash = GetStableHashCode(text);

            // Gets the 32bit fnv1a hash
            // To get it down to 16bit but still reduce hash collisions we cant just cast it to ushort
            // Instead we take the highest 16bits of the 32bit hash and fold them with xor into the lower 16bits
            // This will create a more uniform 16bit hash, the method is described in:
            // http://www.isthe.com/chongo/tech/comp/fnv/ in section "Changing the FNV hash size - xor-folding"
            return (ushort)((hash >> 16) ^ hash);
        }
        #endregion
    }
}
