namespace ZurfurGui;

public static class Helpers
{
    // Mix the hash to try and get better uniformity, hopefully
    // faster than mod with just as much random bit shuffling and mixing.
    // For all 4 billion numbers, it doesn't have any collisions.
    // This is based loosely on xoroshiro64* https://prng.di.unimi.it
    public static int HashMix(int i)
        => (int)((Rol((uint)i, 26) ^ i ^ Rol((uint)i, 9)) * 0x9E3779BB);

    static uint Rol(uint i, int shift)
        => (i << shift) | (i >> (32 - shift));
}
