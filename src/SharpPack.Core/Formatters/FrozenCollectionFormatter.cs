using SharpPack.Formatters;
using SharpPack.Internal;
using System.Collections.Frozen;

// Frozen Collections formatters

namespace SharpPack.Formatters
{
    [Preserve]
    public sealed class FrozenDictionaryFormatter<TKey, TValue> : SharpPackFormatter<FrozenDictionary<TKey, TValue?>>
        where TKey : notnull
    {
        readonly IEqualityComparer<TKey>? equalityComparer;

        public FrozenDictionaryFormatter() : this(null)
        {

        }

        public FrozenDictionaryFormatter(IEqualityComparer<TKey>? equalityComparer)
        {
            this.equalityComparer = equalityComparer;
        }

        [Preserve]
        public override void Serialize<TBufferWriter>(ref SharpPackWriter<TBufferWriter> writer, scoped ref FrozenDictionary<TKey, TValue?>? value)
        {
            if (value == null)
            {
                writer.WriteNullCollectionHeader();
                return;
            }

            var keyFormatter = writer.GetFormatter<TKey>();
            var valueFormatter = writer.GetFormatter<TValue>();

            var count = value.Count;
            writer.WriteCollectionHeader(count);
            var i = 0;
            foreach (var item in value)
            {
                i++;
                KeyValuePairFormatter.Serialize(keyFormatter, valueFormatter, ref writer, item!);
            }

            if (i != count) SharpPackSerializationException.ThrowInvalidConcurrrentCollectionOperation();
        }

        [Preserve]
        public override void Deserialize(ref SharpPackReader reader, scoped ref FrozenDictionary<TKey, TValue?>? value)
        {
            if (!reader.TryReadCollectionHeader(out var length))
            {
                value = null;
                return;
            }

            var dict = new Dictionary<TKey, TValue?>(
                FormatterValidation.InitialCapacity(length),
                equalityComparer);

            var keyFormatter = reader.GetFormatter<TKey>();
            var valueFormatter = reader.GetFormatter<TValue>();
            for (var i = 0; i < length; i++)
            {
                KeyValuePairFormatter.Deserialize(keyFormatter, valueFormatter, ref reader, out var k, out var v);
                dict.Add(k!, v);
            }
            value = dict.ToFrozenDictionary(equalityComparer);
        }
    }

    public sealed class FrozenSetFormatter<T> : SharpPackFormatter<FrozenSet<T?>>
    {
        readonly IEqualityComparer<T?>? equalityComparer;

        public FrozenSetFormatter() : this(null)
        {
        }

        public FrozenSetFormatter(IEqualityComparer<T?>? equalityComparer)
        {
            this.equalityComparer = equalityComparer;
        }

        [Preserve]
        public override void Serialize<TBufferWriter>(ref SharpPackWriter<TBufferWriter> writer, scoped ref FrozenSet<T?>? value)
        {
            if (value == null)
            {
                writer.WriteNullCollectionHeader();
                return;
            }

            var formatter = writer.GetFormatter<T?>();
            writer.WriteCollectionHeader(value.Count);
            foreach (var item in value)
            {
                var v = item;
                formatter.Serialize(ref writer, ref v);
            }
        }

        [Preserve]
        public override void Deserialize(ref SharpPackReader reader, scoped ref FrozenSet<T?>? value)
        {
            if (!reader.TryReadCollectionHeader(out var length))
            {
                value = null;
                return;
            }

            var set = new HashSet<T>(
                FormatterValidation.InitialCapacity(length),
                equalityComparer);

            var formatter = reader.GetFormatter<T?>();
            for (int i = 0; i < length; i++)
            {
                T? v = default;
                formatter.Deserialize(ref reader, ref v);
                set.Add(v!);
            }

            value = set.ToFrozenSet(equalityComparer)!;
        }
    }
}
