// WARNING: Do not modify! Generated file.

namespace UnityEngine.Purchasing.Security {
    public class GooglePlayTangle
    {
        private static byte[] data = System.Convert.FromBase64String("/vSpFfnWu0n1AULVLEwAcoGtuAr8Z6AcuRwf5QHig/sFX2pAIAfS8j2SNkcvDal+6qIkNfxi0yro8ufih6Zkl/AhVBNfN8z2PVxTiVj+ZiqG+nQL4e7h9qBPEBxZBNN/2cH6Dv6Xi43yUCKh/p3oADGgHMcQrjnbB/ciiyZOrpYV7oGRm6HNFc/0VkReyUXwXInNnG+nmhDSb+BLHNUgOEv5ellLdn1yUf0z/Yx2enp6fnt4pKQiVpeeaztiKrlHaGXibyaQH+zCxfExFw1PxHF9r/KlYt9uo0bWaGZfH0LhOhT1EzWqD3FntlIV9yLu+Xp0e0v5enF5+Xp6e9dOi3D1IDyu6FNfcTkOpbXD7bArb4tD/URG24rMrny1hRzx8nl4ent6");
        private static int[] order = new int[] { 2,7,13,11,11,8,9,8,13,11,12,11,13,13,14 };
        private static int key = 123;

        public static readonly bool IsPopulated = true;

        public static byte[] Data() {
        	if (IsPopulated == false)
        		return null;
            return Obfuscator.DeObfuscate(data, order, key);
        }
    }
}
