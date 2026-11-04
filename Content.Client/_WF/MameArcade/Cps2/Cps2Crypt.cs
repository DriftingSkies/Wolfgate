// Port of MAME's src/mame/capcom/cps2crypt.cpp.
// license:BSD-3-Clause
// copyright-holders:Paul Leaman, Andreas Naive, Nicola Salmoria, Charles MacDonald

namespace Content.Client._WF.MameArcade.Cps2;

/// <summary>
/// CPS2 program encryption. The board decrypts only opcode fetches, through two Feistel networks keyed by the
/// address and a per-game 64-bit key, so the decrypted opcodes go in their own region beside the raw program.
/// </summary>
public static class Cps2Crypt
{
    private static readonly int[] Fn1GroupA = { 10, 4, 6, 7, 2, 13, 15, 14 };
    private static readonly int[] Fn1GroupB = { 0, 1, 3, 5, 8, 9, 11, 12 };
    private static readonly int[] Fn2GroupA = { 6, 0, 2, 13, 1, 4, 14, 7 };
    private static readonly int[] Fn2GroupB = { 3, 5, 9, 10, 8, 15, 12, 11 };

    private static readonly int[] FirstKeyBits =
    {
        33, 58, 49, 36, 0, 31, 22, 30, 3, 16, 5, 53, 10, 41, 23, 19, 27, 39, 43, 6, 34, 12, 61, 21,
        48, 13, 32, 35, 6, 42, 43, 14, 21, 41, 52, 25, 18, 47, 46, 37, 57, 53, 20, 8, 55, 54, 59, 60,
        27, 33, 35, 18, 8, 15, 63, 1, 50, 44, 16, 46, 5, 4, 45, 51, 38, 25, 13, 11, 62, 29, 48, 2,
        59, 61, 62, 56, 51, 57, 54, 9, 24, 63, 22, 7, 26, 42, 45, 40, 23, 14, 2, 31, 52, 28, 44, 17,
    };

    private static readonly int[] SecondKeyBits =
    {
        34, 9, 32, 24, 44, 54, 38, 61, 47, 13, 28, 7, 29, 58, 18, 1, 20, 60, 15, 6, 11, 43, 39, 19,
        63, 23, 16, 62, 54, 40, 31, 3, 56, 61, 17, 25, 47, 38, 55, 57, 5, 4, 15, 42, 22, 7, 2, 19,
        46, 37, 29, 39, 12, 30, 49, 57, 31, 41, 26, 27, 24, 36, 11, 63, 33, 16, 56, 62, 48, 60, 59, 32,
        12, 30, 53, 48, 10, 0, 50, 35, 3, 59, 14, 49, 51, 45, 44, 2, 21, 33, 55, 52, 23, 28, 8, 26,
    };

    // Each row is a permutation of the seed's bits.
    private static readonly int[] SubkeyBits =
    {
        5, 10, 14, 9, 4, 0, 15, 6, 1, 8, 3, 2, 12, 7, 13, 11,
        5, 12, 7, 2, 13, 11, 9, 14, 4, 1, 6, 10, 8, 0, 15, 3,
        4, 10, 2, 0, 6, 9, 12, 1, 11, 7, 15, 8, 13, 5, 14, 3,
        14, 11, 12, 7, 4, 5, 2, 10, 1, 15, 0, 9, 8, 6, 13, 3,
    };

    /// <summary>The key a MAME .key file holds, or null for a board whose key battery died.</summary>
    public static Cps2Key? FromKeyFile(byte[] file)
    {
        if (file.Length < 20)
            return null;

        var decoded = new ushort[10];
        for (var b = 0; b < 10 * 16; b++)
        {
            var bit = (317 - b) % 160;
            if (((file[bit / 8] >> ((bit ^ 7) % 8)) & 1) != 0)
                decoded[b / 16] |= (ushort) (0x8000 >> (b % 16));
        }

        if (decoded[9] == 0xffff)
            return null;

        var upper = (((~decoded[9] & 0x3ff) << 14) | 0x3fff) + 1;
        return new Cps2Key((uint) decoded[0] << 16 | decoded[1], (uint) decoded[2] << 16 | decoded[3], upper);
    }

    /// <summary>
    /// The opcodes the board's 68000 fetches from <paramref name="rom"/>, a big-endian program region. Words past
    /// the encrypted range come out unchanged.
    /// </summary>
    public static byte[] Decrypt(byte[] rom, Cps2Key key)
    {
        var boxes1 = new[] { Optimise(Fn1R1), Optimise(Fn1R2), Optimise(Fn1R3), Optimise(Fn1R4) };
        var boxes2 = new[] { Optimise(Fn2R1), Optimise(Fn2R2), Optimise(Fn2R3), Optimise(Fn2R4) };
        var master = new[] { key.Key0, key.Key1 };

        var key1 = Expand(FirstKeyBits, master);

        // Extra bits for the s-boxes with fewer than six inputs.
        key1[0] ^= Bit(key1[0], 1) << 4;
        key1[0] ^= Bit(key1[0], 2) << 5;
        key1[0] ^= Bit(key1[0], 8) << 11;
        key1[1] ^= Bit(key1[1], 0) << 5;
        key1[1] ^= Bit(key1[1], 8) << 11;
        key1[2] ^= Bit(key1[2], 1) << 5;
        key1[2] ^= Bit(key1[2], 8) << 11;

        var words = rom.Length / 2;
        var upper = key.UpperLimit / 2;
        var dec = (byte[]) rom.Clone();
        var subkey = new uint[2];

        for (var i = 0; i < 0x10000; i++)
        {
            // The address through the first network seeds the key for the second.
            var seed = Feistel((ushort) i, Fn1GroupA, Fn1GroupB, boxes1, key1);

            subkey[0] = 0;
            subkey[1] = 0;
            for (var b = 0; b < 64; b++)
            {
                subkey[b / 32] |= Bit(seed, SubkeyBits[b]) << (b % 32);
            }

            subkey[0] ^= master[0];
            subkey[1] ^= master[1];

            var key2 = Expand(SecondKeyBits, subkey);
            key2[0] ^= Bit(key2[0], 0) << 5;
            key2[0] ^= Bit(key2[0], 6) << 11;
            key2[1] ^= Bit(key2[1], 0) << 5;
            key2[1] ^= Bit(key2[1], 1) << 4;
            key2[2] ^= Bit(key2[2], 2) << 5;
            key2[2] ^= Bit(key2[2], 3) << 4;
            key2[2] ^= Bit(key2[2], 7) << 11;
            key2[3] ^= Bit(key2[3], 1) << 5;

            for (var a = i; a < words && a <= upper; a += 0x10000)
            {
                var word = (ushort) (rom[a * 2] << 8 | rom[a * 2 + 1]);
                var plain = Feistel(word, Fn2GroupA, Fn2GroupB, boxes2, key2);
                dec[a * 2] = (byte) (plain >> 8);
                dec[a * 2 + 1] = (byte) plain;
            }
        }

        return dec;
    }

    private static uint Bit(uint value, int bit) => (value >> bit) & 1;

    /// <summary>A 64-bit key spread into four 24-bit s-box keys.</summary>
    private static uint[] Expand(int[] bits, uint[] source)
    {
        var key = new uint[4];
        for (var i = 0; i < 96; i++)
        {
            key[i / 24] |= Bit(source[bits[i] / 32], bits[i] % 32) << (i % 24);
        }

        return key;
    }

    private static ushort Feistel(ushort value, int[] bitsA, int[] bitsB, OptimisedSbox[][] boxes, uint[] key)
    {
        var l = Gather(value, bitsB);
        var r = Gather(value, bitsA);

        l ^= Fn(r, boxes[0], key[0]);
        r ^= Fn(l, boxes[1], key[1]);
        l ^= Fn(r, boxes[2], key[2]);
        r ^= Fn(l, boxes[3], key[3]);

        var result = 0;
        for (var i = 0; i < 8; i++)
        {
            result |= ((l >> i) & 1) << bitsA[i];
            result |= ((r >> i) & 1) << bitsB[i];
        }

        return (ushort) result;
    }

    private static int Gather(ushort value, int[] bits)
    {
        var result = 0;
        for (var i = 0; i < 8; i++)
        {
            result |= ((value >> bits[i]) & 1) << i;
        }

        return result;
    }

    private static int Fn(int input, OptimisedSbox[] boxes, uint key)
    {
        return boxes[0].Fn(input, key) | boxes[1].Fn(input, key >> 6) | boxes[2].Fn(input, key >> 12) | boxes[3].Fn(input, key >> 18);
    }

    private static OptimisedSbox[] Optimise(Sbox[] boxes)
    {
        var optimised = new OptimisedSbox[boxes.Length];
        for (var i = 0; i < boxes.Length; i++)
        {
            optimised[i] = new OptimisedSbox(boxes[i]);
        }

        return optimised;
    }

    /// <summary>An s-box as the hardware wires it: which input bits it reads and which output bits it sets.</summary>
    private sealed class Sbox(byte[] table, int[] inputs, int[] outputs)
    {
        public readonly byte[] Table = table;
        public readonly int[] Inputs = inputs;
        public readonly int[] Outputs = outputs;
    }

    /// <summary>An s-box with its input gathering and output placement precalculated.</summary>
    private sealed class OptimisedSbox
    {
        private readonly byte[] _inputLookup = new byte[256];
        private readonly byte[] _output = new byte[64];

        public OptimisedSbox(Sbox box)
        {
            for (var i = 0; i < 256; i++)
            {
                var res = 0;
                for (var b = 0; b < 6; b++)
                {
                    if (box.Inputs[b] >= 0)
                        res |= ((i >> box.Inputs[b]) & 1) << b;
                }

                _inputLookup[i] = (byte) res;
            }

            for (var i = 0; i < 64; i++)
            {
                var o = box.Table[i];
                if ((o & 1) != 0)
                    _output[i] |= (byte) (1 << box.Outputs[0]);
                if ((o & 2) != 0)
                    _output[i] |= (byte) (1 << box.Outputs[1]);
            }
        }

        public int Fn(int input, uint key) => _output[_inputLookup[input] ^ (key & 0x3f)];
    }

    private static readonly Sbox[] Fn1R1 =
    {
        new(
            new byte[] { 0, 2, 2, 0, 1, 0, 1, 1, 3, 2, 0, 3, 0, 3, 1, 2, 1, 1, 1, 2, 1, 3, 2, 2, 2, 3, 3, 2, 1, 1, 1, 2,
                2, 2, 0, 0, 3, 1, 3, 1, 1, 1, 3, 0, 0, 1, 0, 0, 1, 2, 2, 1, 2, 3, 2, 2, 2, 3, 1, 3, 2, 0, 1, 3 },
            new[] { 3, 4, 5, 6, -1, -1 },
            new[] { 3, 6 }),
        new(
            new byte[] { 3, 0, 2, 2, 2, 1, 1, 1, 1, 2, 1, 0, 0, 0, 2, 3, 2, 3, 1, 3, 0, 0, 0, 2, 1, 2, 2, 3, 0, 3, 3, 3,
                0, 1, 3, 2, 3, 3, 3, 1, 1, 1, 1, 2, 0, 1, 2, 1, 3, 2, 3, 1, 1, 3, 2, 2, 2, 3, 1, 3, 2, 3, 0, 0 },
            new[] { 0, 1, 2, 4, 7, -1 },
            new[] { 2, 7 }),
        new(
            new byte[] { 3, 0, 3, 1, 1, 0, 2, 2, 3, 1, 2, 0, 3, 3, 2, 3, 0, 1, 0, 1, 2, 3, 0, 2, 0, 2, 0, 1, 0, 0, 1, 0,
                2, 3, 1, 2, 1, 0, 2, 0, 2, 1, 0, 1, 0, 2, 1, 0, 3, 1, 2, 3, 1, 3, 1, 1, 1, 2, 0, 2, 2, 0, 0, 0 },
            new[] { 0, 1, 2, 3, 6, 7 },
            new[] { 0, 1 }),
        new(
            new byte[] { 3, 2, 0, 3, 0, 2, 2, 1, 1, 2, 3, 2, 1, 3, 2, 1, 2, 2, 1, 3, 3, 2, 1, 0, 1, 0, 1, 3, 0, 0, 0, 2,
                2, 1, 0, 1, 0, 1, 0, 1, 3, 1, 1, 2, 2, 3, 2, 0, 3, 3, 2, 0, 2, 1, 3, 3, 0, 0, 3, 0, 1, 1, 3, 3 },
            new[] { 0, 1, 3, 5, 6, 7 },
            new[] { 4, 5 }),
    };

    private static readonly Sbox[] Fn1R2 =
    {
        new(
            new byte[] { 3, 3, 2, 0, 3, 0, 3, 1, 0, 3, 0, 1, 0, 2, 1, 3, 1, 3, 0, 3, 3, 1, 3, 3, 3, 2, 3, 2, 2, 3, 1, 2,
                0, 2, 2, 1, 0, 1, 2, 0, 3, 3, 0, 1, 3, 2, 1, 2, 3, 0, 1, 3, 0, 1, 2, 2, 1, 2, 1, 2, 0, 1, 3, 0 },
            new[] { 0, 1, 2, 3, 6, -1 },
            new[] { 1, 6 }),
        new(
            new byte[] { 1, 2, 3, 2, 1, 3, 0, 1, 1, 0, 2, 0, 0, 2, 3, 2, 3, 3, 0, 1, 2, 2, 1, 0, 1, 0, 1, 2, 3, 2, 1, 3,
                2, 2, 2, 0, 1, 0, 2, 3, 2, 1, 2, 1, 2, 1, 0, 3, 0, 1, 2, 3, 1, 2, 1, 3, 2, 0, 3, 2, 3, 0, 2, 0 },
            new[] { 2, 4, 5, 6, 7, -1 },
            new[] { 5, 7 }),
        new(
            new byte[] { 0, 1, 0, 2, 1, 1, 0, 1, 0, 2, 2, 2, 1, 3, 0, 0, 1, 1, 3, 1, 2, 2, 2, 3, 1, 0, 3, 3, 3, 2, 2, 2,
                1, 1, 3, 0, 3, 1, 3, 0, 1, 3, 3, 2, 1, 1, 0, 0, 1, 2, 2, 2, 1, 1, 1, 2, 2, 0, 0, 3, 2, 3, 1, 3 },
            new[] { 1, 2, 3, 4, 5, 7 },
            new[] { 0, 3 }),
        new(
            new byte[] { 2, 1, 0, 3, 3, 3, 2, 0, 1, 2, 1, 1, 1, 0, 3, 1, 1, 3, 3, 0, 1, 2, 1, 0, 0, 0, 3, 0, 3, 0, 3, 0,
                1, 3, 3, 3, 0, 3, 2, 0, 2, 1, 2, 2, 2, 1, 1, 3, 0, 1, 0, 1, 0, 1, 1, 1, 1, 3, 1, 0, 1, 2, 3, 3 },
            new[] { 0, 1, 3, 4, 6, 7 },
            new[] { 2, 4 }),
    };

    private static readonly Sbox[] Fn1R3 =
    {
        new(
            new byte[] { 0, 0, 0, 3, 3, 1, 1, 0, 2, 0, 2, 0, 0, 0, 3, 2, 0, 1, 2, 3, 2, 2, 1, 0, 3, 0, 0, 0, 0, 0, 2, 3,
                3, 0, 0, 1, 1, 2, 3, 3, 0, 1, 3, 2, 0, 1, 3, 3, 2, 0, 0, 1, 0, 2, 0, 0, 0, 3, 1, 3, 3, 3, 3, 3 },
            new[] { 0, 1, 5, 6, 7, -1 },
            new[] { 0, 5 }),
        new(
            new byte[] { 2, 3, 2, 3, 0, 2, 3, 0, 2, 2, 3, 0, 3, 2, 0, 2, 1, 0, 2, 3, 1, 1, 1, 0, 0, 1, 0, 2, 1, 2, 2, 1,
                3, 0, 2, 1, 2, 3, 3, 0, 3, 2, 3, 1, 0, 2, 1, 0, 1, 2, 2, 3, 0, 2, 1, 3, 1, 3, 0, 2, 1, 1, 1, 3 },
            new[] { 2, 3, 4, 6, 7, -1 },
            new[] { 6, 7 }),
        new(
            new byte[] { 3, 0, 2, 1, 1, 3, 1, 2, 2, 1, 2, 2, 2, 0, 0, 1, 2, 3, 1, 0, 2, 0, 0, 2, 3, 1, 2, 0, 0, 0, 3, 0,
                2, 1, 1, 2, 0, 0, 1, 2, 3, 1, 1, 2, 0, 1, 3, 0, 3, 1, 1, 0, 0, 2, 3, 0, 0, 0, 0, 3, 2, 0, 0, 0 },
            new[] { 0, 2, 3, 4, 5, 6 },
            new[] { 1, 4 }),
        new(
            new byte[] { 0, 1, 0, 0, 2, 1, 3, 2, 3, 3, 2, 1, 0, 1, 1, 1, 1, 1, 0, 3, 3, 1, 1, 0, 0, 2, 2, 1, 0, 3, 3, 2,
                1, 3, 3, 0, 3, 0, 2, 1, 1, 2, 3, 2, 2, 2, 1, 0, 0, 3, 3, 3, 2, 2, 3, 1, 0, 2, 3, 0, 3, 1, 1, 0 },
            new[] { 0, 1, 2, 3, 5, 7 },
            new[] { 2, 3 }),
    };

    private static readonly Sbox[] Fn1R4 =
    {
        new(
            new byte[] { 1, 1, 1, 1, 1, 0, 1, 3, 3, 2, 3, 0, 1, 2, 0, 2, 3, 3, 0, 1, 2, 1, 2, 3, 0, 3, 2, 3, 2, 0, 1, 2,
                0, 1, 0, 3, 2, 1, 3, 2, 3, 1, 2, 3, 2, 0, 1, 2, 2, 0, 0, 0, 2, 1, 3, 0, 3, 1, 3, 0, 1, 3, 3, 0 },
            new[] { 1, 2, 3, 4, 5, 7 },
            new[] { 0, 4 }),
        new(
            new byte[] { 3, 0, 0, 0, 0, 1, 0, 2, 3, 3, 1, 3, 0, 3, 1, 2, 2, 2, 3, 1, 0, 0, 2, 0, 1, 0, 2, 2, 3, 3, 0, 0,
                1, 1, 3, 0, 2, 3, 0, 3, 0, 3, 0, 2, 0, 2, 0, 1, 0, 3, 0, 1, 3, 1, 1, 0, 0, 1, 3, 3, 2, 2, 1, 0 },
            new[] { 0, 1, 2, 3, 5, 6 },
            new[] { 1, 3 }),
        new(
            new byte[] { 0, 1, 1, 2, 0, 1, 3, 1, 2, 0, 3, 2, 0, 0, 3, 0, 3, 0, 1, 2, 2, 3, 3, 2, 3, 2, 0, 1, 0, 0, 1, 0,
                3, 0, 2, 3, 0, 2, 2, 2, 1, 1, 0, 2, 2, 0, 0, 1, 2, 1, 1, 1, 2, 3, 0, 3, 1, 2, 3, 3, 1, 1, 3, 0 },
            new[] { 0, 2, 4, 5, 6, 7 },
            new[] { 2, 6 }),
        new(
            new byte[] { 0, 1, 2, 2, 0, 1, 0, 3, 2, 2, 1, 1, 3, 2, 0, 2, 0, 1, 3, 3, 0, 2, 2, 3, 3, 2, 0, 0, 2, 1, 3, 3,
                1, 1, 1, 3, 1, 2, 1, 1, 0, 3, 3, 2, 3, 2, 3, 0, 3, 1, 0, 0, 3, 0, 0, 0, 2, 2, 2, 1, 2, 3, 0, 0 },
            new[] { 0, 1, 3, 4, 6, 7 },
            new[] { 5, 7 }),
    };

    private static readonly Sbox[] Fn2R1 =
    {
        new(
            new byte[] { 2, 0, 2, 0, 3, 0, 0, 3, 1, 1, 0, 1, 3, 2, 0, 1, 2, 0, 1, 2, 0, 2, 0, 2, 2, 2, 3, 0, 2, 1, 3, 0,
                0, 1, 0, 1, 2, 2, 3, 3, 0, 3, 0, 2, 3, 0, 1, 2, 1, 1, 0, 2, 0, 3, 1, 1, 2, 2, 1, 3, 1, 1, 3, 1 },
            new[] { 0, 3, 4, 5, 7, -1 },
            new[] { 6, 7 }),
        new(
            new byte[] { 1, 1, 0, 3, 0, 2, 0, 1, 3, 0, 2, 0, 1, 1, 0, 0, 1, 3, 2, 2, 0, 2, 2, 2, 2, 0, 1, 3, 3, 3, 1, 1,
                1, 3, 1, 3, 2, 2, 2, 2, 2, 2, 0, 1, 0, 1, 1, 2, 3, 1, 1, 2, 0, 3, 3, 3, 2, 2, 3, 1, 1, 1, 3, 0 },
            new[] { 1, 2, 3, 4, 6, -1 },
            new[] { 3, 5 }),
        new(
            new byte[] { 1, 0, 2, 2, 3, 3, 3, 3, 1, 2, 2, 1, 0, 1, 2, 1, 1, 2, 3, 1, 2, 0, 0, 1, 2, 3, 1, 2, 0, 0, 0, 2,
                2, 0, 1, 1, 0, 0, 2, 0, 0, 0, 2, 3, 2, 3, 0, 1, 3, 0, 0, 0, 2, 3, 2, 0, 1, 3, 2, 1, 3, 1, 1, 3 },
            new[] { 1, 2, 4, 5, 6, 7 },
            new[] { 1, 4 }),
        new(
            new byte[] { 1, 3, 3, 0, 3, 2, 3, 1, 3, 2, 1, 1, 3, 3, 2, 1, 2, 3, 0, 3, 1, 0, 0, 2, 3, 0, 0, 0, 3, 3, 0, 1,
                2, 3, 0, 0, 0, 1, 2, 1, 3, 0, 0, 1, 0, 2, 2, 2, 3, 3, 1, 2, 1, 3, 0, 0, 0, 3, 0, 1, 3, 2, 2, 0 },
            new[] { 0, 2, 3, 5, 6, 7 },
            new[] { 0, 2 }),
    };

    private static readonly Sbox[] Fn2R2 =
    {
        new(
            new byte[] { 3, 1, 3, 0, 3, 0, 3, 1, 3, 0, 0, 1, 1, 3, 0, 3, 1, 1, 0, 1, 2, 3, 2, 3, 3, 1, 2, 2, 2, 0, 2, 3,
                2, 2, 2, 1, 1, 3, 3, 0, 3, 1, 2, 1, 1, 1, 0, 2, 0, 3, 3, 0, 0, 2, 0, 0, 1, 1, 2, 1, 2, 1, 1, 0 },
            new[] { 0, 2, 4, 6, -1, -1 },
            new[] { 4, 6 }),
        new(
            new byte[] { 0, 3, 0, 3, 3, 2, 1, 2, 3, 1, 1, 1, 2, 0, 2, 3, 0, 3, 1, 2, 2, 1, 3, 3, 3, 2, 1, 2, 2, 0, 1, 0,
                2, 3, 0, 1, 2, 0, 1, 1, 2, 0, 2, 1, 2, 0, 2, 3, 3, 1, 0, 2, 3, 3, 0, 3, 1, 1, 3, 0, 0, 1, 2, 0 },
            new[] { 1, 3, 4, 5, 6, 7 },
            new[] { 0, 3 }),
        new(
            new byte[] { 0, 0, 2, 1, 3, 2, 1, 0, 1, 2, 2, 2, 1, 1, 0, 3, 1, 2, 2, 3, 2, 1, 1, 0, 3, 0, 0, 1, 1, 2, 3, 1,
                3, 3, 2, 2, 1, 0, 1, 1, 1, 2, 0, 1, 2, 3, 0, 3, 3, 0, 3, 2, 2, 0, 2, 2, 1, 2, 3, 2, 1, 0, 2, 1 },
            new[] { 0, 1, 3, 4, 5, 7 },
            new[] { 1, 7 }),
        new(
            new byte[] { 0, 2, 1, 2, 0, 2, 2, 0, 1, 3, 2, 0, 3, 2, 3, 0, 3, 3, 2, 3, 1, 2, 3, 1, 2, 2, 0, 0, 2, 2, 1, 2,
                2, 3, 3, 3, 1, 1, 0, 0, 0, 3, 2, 0, 3, 2, 3, 1, 1, 1, 1, 0, 1, 0, 1, 3, 0, 0, 1, 2, 2, 3, 2, 0 },
            new[] { 1, 2, 3, 5, 6, 7 },
            new[] { 2, 5 }),
    };

    private static readonly Sbox[] Fn2R3 =
    {
        new(
            new byte[] { 2, 1, 2, 1, 2, 3, 1, 3, 2, 2, 1, 3, 3, 0, 0, 1, 0, 2, 0, 3, 3, 1, 0, 0, 1, 1, 0, 2, 3, 2, 1, 2,
                1, 1, 2, 1, 1, 3, 2, 2, 0, 2, 2, 3, 3, 3, 2, 0, 0, 0, 0, 0, 3, 3, 3, 0, 1, 2, 1, 0, 2, 3, 3, 1 },
            new[] { 2, 3, 4, 6, -1, -1 },
            new[] { 3, 5 }),
        new(
            new byte[] { 3, 2, 3, 3, 1, 0, 3, 0, 2, 0, 1, 1, 1, 0, 3, 0, 3, 1, 3, 1, 0, 1, 2, 3, 2, 2, 3, 2, 0, 1, 1, 2,
                3, 0, 0, 2, 1, 0, 0, 2, 2, 0, 1, 0, 0, 2, 0, 0, 1, 3, 1, 3, 2, 0, 3, 3, 1, 0, 2, 2, 2, 3, 0, 0 },
            new[] { 0, 1, 3, 5, 7, -1 },
            new[] { 0, 2 }),
        new(
            new byte[] { 2, 2, 1, 0, 2, 3, 3, 0, 0, 0, 1, 3, 1, 2, 3, 2, 2, 3, 1, 3, 0, 3, 0, 3, 3, 2, 2, 1, 0, 0, 0, 2,
                1, 2, 2, 2, 0, 0, 1, 2, 0, 1, 3, 0, 2, 3, 2, 1, 3, 2, 2, 2, 3, 1, 3, 0, 2, 0, 2, 1, 0, 3, 3, 1 },
            new[] { 0, 1, 2, 3, 5, 7 },
            new[] { 1, 6 }),
        new(
            new byte[] { 1, 2, 3, 2, 0, 2, 1, 3, 3, 1, 0, 1, 1, 2, 2, 0, 0, 1, 1, 1, 2, 1, 1, 2, 0, 1, 3, 3, 1, 1, 1, 2,
                3, 3, 1, 0, 2, 1, 1, 1, 2, 1, 0, 0, 2, 2, 3, 2, 3, 2, 2, 0, 2, 2, 3, 3, 0, 2, 3, 0, 2, 2, 1, 1 },
            new[] { 0, 2, 4, 5, 6, 7 },
            new[] { 4, 7 }),
    };

    private static readonly Sbox[] Fn2R4 =
    {
        new(
            new byte[] { 2, 0, 1, 1, 2, 1, 3, 3, 1, 1, 1, 2, 0, 1, 0, 2, 0, 1, 2, 0, 2, 3, 0, 2, 3, 3, 2, 2, 3, 2, 0, 1,
                3, 0, 2, 0, 2, 3, 1, 3, 2, 0, 0, 1, 1, 2, 3, 1, 1, 1, 0, 1, 2, 0, 3, 3, 1, 1, 1, 3, 3, 1, 1, 0 },
            new[] { 0, 1, 3, 6, 7, -1 },
            new[] { 0, 3 }),
        new(
            new byte[] { 1, 2, 2, 1, 0, 3, 3, 1, 0, 2, 2, 2, 1, 0, 1, 0, 1, 1, 0, 1, 0, 2, 1, 0, 2, 1, 0, 2, 3, 2, 3, 3,
                2, 2, 1, 2, 2, 3, 1, 3, 3, 3, 0, 1, 0, 1, 3, 0, 0, 0, 1, 2, 0, 3, 3, 2, 3, 2, 1, 3, 2, 1, 0, 2 },
            new[] { 0, 1, 2, 4, 5, 6 },
            new[] { 4, 7 }),
        new(
            new byte[] { 2, 3, 2, 1, 3, 2, 3, 0, 0, 2, 1, 1, 0, 0, 3, 2, 3, 1, 0, 1, 2, 2, 2, 1, 3, 2, 2, 1, 0, 2, 1, 2,
                0, 3, 1, 0, 0, 3, 1, 1, 3, 3, 2, 0, 1, 0, 1, 3, 0, 0, 1, 2, 1, 2, 3, 2, 1, 0, 0, 3, 2, 1, 1, 3 },
            new[] { 0, 2, 3, 4, 5, 7 },
            new[] { 1, 2 }),
        new(
            new byte[] { 2, 0, 0, 3, 2, 2, 2, 1, 3, 3, 1, 1, 2, 0, 0, 3, 1, 0, 3, 2, 1, 0, 2, 0, 3, 2, 2, 3, 2, 0, 3, 0,
                1, 3, 0, 2, 2, 1, 3, 3, 0, 1, 0, 3, 1, 1, 3, 2, 0, 3, 0, 2, 3, 2, 1, 3, 2, 3, 0, 0, 1, 3, 2, 1 },
            new[] { 2, 3, 4, 5, 6, 7 },
            new[] { 5, 6 }),
    };
}
