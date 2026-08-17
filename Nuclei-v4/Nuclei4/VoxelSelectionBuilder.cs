using System;
using System.Threading;

namespace Nuclei4
{
    internal sealed class VoxelSelectionBuilder
    {
        readonly int count;
        int[] words;

        public VoxelSelectionBuilder(int count)
        {
            if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
            this.count = count;
            words = new int[(count + 31) >> 5];
        }

        public void Set(int flatIndex)
        {
            if (flatIndex < 0 || flatIndex >= count) return;
            words[flatIndex >> 5] |= unchecked((int)(1u << (flatIndex & 31)));
        }

        public void SetThreadSafe(int flatIndex)
        {
            if (flatIndex < 0 || flatIndex >= count) return;

            int wordIndex = flatIndex >> 5;
            int mask = unchecked((int)(1u << (flatIndex & 31)));
            int original = Volatile.Read(ref words[wordIndex]);
            while ((original & mask) == 0)
            {
                int updated = original | mask;
                int observed = Interlocked.CompareExchange(ref words[wordIndex], updated, original);
                if (observed == original) return;
                original = observed;
            }
        }

        public bool Contains(int flatIndex)
        {
            return flatIndex >= 0 &&
                   flatIndex < count &&
                   (words[flatIndex >> 5] & unchecked((int)(1u << (flatIndex & 31)))) != 0;
        }

        public void Fill()
        {
            for (int i = 0; i < words.Length; i++) words[i] = -1;
            MaskUnusedBits();
        }

        public void Clear()
        {
            Array.Clear(words, 0, words.Length);
        }

        public void UnionWith(VoxelGridData data)
        {
            ValidateGrid(data);
            data.OrActiveSelection(words);
        }

        public void IntersectWith(VoxelGridData data)
        {
            ValidateGrid(data);
            data.AndActiveSelection(words);
        }

        public void ExceptWith(VoxelGridData data)
        {
            ValidateGrid(data);
            data.AndNotActiveSelection(words);
        }

        public void Invert()
        {
            for (int i = 0; i < words.Length; i++) words[i] = ~words[i];
            MaskUnusedBits();
        }

        public void Filter(Func<int, bool> keep)
        {
            if (keep == null) throw new ArgumentNullException(nameof(keep));

            for (int wordIndex = 0; wordIndex < words.Length; wordIndex++)
            {
                uint pending = unchecked((uint)words[wordIndex]);
                uint kept = 0;
                while (pending != 0)
                {
                    int bit = TrailingZeroCount(pending);
                    int flatIndex = (wordIndex << 5) + bit;
                    if (flatIndex < count && keep(flatIndex)) kept |= 1u << bit;
                    pending &= pending - 1;
                }

                words[wordIndex] = unchecked((int)kept);
            }
        }

        public VoxelGridData ApplyTo(VoxelGridData source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (source.Count != count) throw new ArgumentException("Selection size does not match the voxel field.", nameof(source));

            int[] ownedWords = words;
            words = Array.Empty<int>();
            return source.WithActiveWords(ownedWords);
        }

        void ValidateGrid(VoxelGridData data)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (data.Count != count) throw new ArgumentException("Selection size does not match the voxel field.", nameof(data));
        }

        void MaskUnusedBits()
        {
            if (words.Length == 0) return;
            int usedBits = count & 31;
            if (usedBits == 0) return;
            uint mask = (1u << usedBits) - 1u;
            words[words.Length - 1] &= unchecked((int)mask);
        }

        static int TrailingZeroCount(uint value)
        {
            int count = 0;
            while ((value & 1u) == 0)
            {
                value >>= 1;
                count++;
            }

            return count;
        }
    }
}
