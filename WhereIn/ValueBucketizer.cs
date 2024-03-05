public class ValueBucketizer : IValueBucketizer
{
    public static readonly ValueBucketizer Instance = new();

    private ValueBucketizer() { }

    public T[] Bucketize<T>(T[] values)
    {
        var distinctValues = values.Distinct().ToArray();
        var wellKnownBucketSizes = new[] { 0, 50, 51, 100, 101 };
        foreach (var bucketSize in wellKnownBucketSizes)
        {
            if (bucketSize == distinctValues.Length)
            {
                return distinctValues;
            }
        }

        var exponentialBucketSize = 1;
        while (exponentialBucketSize < distinctValues.Length)
        {
            exponentialBucketSize *= 2;
        }

        var bucket = new T[exponentialBucketSize];
        Array.Copy(distinctValues, bucket, distinctValues.Length);

        for (var i = distinctValues.Length; i < exponentialBucketSize; i++)
        {
            bucket[i] = distinctValues[0];
        }

        return bucket;
    }
}
