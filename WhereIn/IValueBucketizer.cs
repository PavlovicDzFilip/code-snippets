namespace WhereIn;

public interface IValueBucketizer
{
    T[] Bucketize<T>(T[] values);
}