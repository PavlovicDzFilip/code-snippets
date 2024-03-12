using System.Linq.Expressions;
using System.Reflection;

namespace WhereIn;

public static class QueryableExtensions
{
    private static readonly MethodInfo ContainsGenericMethod = typeof(Enumerable)
        .GetMethods()
        .Single(m => m.Name == "Contains" && m.GetParameters().Length == 2);

    public static IQueryable<TQuery> WhereIn<TQuery, TKey>(
        this IQueryable<TQuery> queryable,
        Expression<Func<TQuery, TKey>> keySelector,
        IEnumerable<TKey> values,
        IValueBucketizer valueBucketizer)
        where TKey : notnull
    {
        var distinctValues = values.Distinct().ToArray();
        if (!distinctValues.Any())
        {
            return queryable.Where(x => false);
        }

        var hardcodeValuesTreshold = 1024;
        if (distinctValues.Length > hardcodeValuesTreshold)
        {
            return ApplyContainsAsAFallback(queryable, keySelector, distinctValues);
        }

        var conditionalExpressions = valueBucketizer.Bucketize(distinctValues)
            .Select(v =>
            {
                Expression<Func<TKey>> valueAsExpression = () => v;
                return Expression.Equal(keySelector.Body, valueAsExpression.Body);
            })
            .ToArray();

        var body = AggregateConditionsIntoOrElseTree(conditionalExpressions);

        var whereClause = Expression.Lambda<Func<TQuery, bool>>(body, keySelector.Parameters);
        return queryable.Where(whereClause);
    }

    public static IQueryable<TQuery> WhereIn<TQuery, TKey>(
        this IQueryable<TQuery> queryable,
        Expression<Func<TQuery, TKey>> keySelector,
        IEnumerable<TKey> values)
        where TKey : notnull
        => WhereIn(queryable, keySelector, values, ValueBucketizer.Instance);


    public static IQueryable<TQuery> WhereNotIn<TQuery, TKey>(
        this IQueryable<TQuery> queryable,
        Expression<Func<TQuery, TKey>> keySelector,
        IEnumerable<TKey> values)
        where TKey : notnull
        => WhereNotIn(queryable, keySelector, values, ValueBucketizer.Instance);

    public static IQueryable<TQuery> WhereNotIn<TQuery, TKey>(
        this IQueryable<TQuery> queryable,
        Expression<Func<TQuery, TKey>> keySelector,
        IEnumerable<TKey> values,
        IValueBucketizer valueBucketizer)
        where TKey : notnull
    {
        var distinctValues = values.Distinct().ToArray();
        if (!distinctValues.Any())
        {
            return queryable;
        }

        var hardcodeValuesTreshold = 1024;
        if (distinctValues.Length > hardcodeValuesTreshold)
        {
            return ApplyNotContainsAsAFallback(queryable, keySelector, distinctValues);
        }

        var conditionalExpressions = valueBucketizer.Bucketize(distinctValues)
            .Select(v =>
            {
                Expression<Func<TKey>> valueAsExpression = () => v;
                return Expression.Equal(keySelector.Body, valueAsExpression.Body);
            })
            .ToArray();

        var body = AggregateConditionsIntoOrElseTree(conditionalExpressions);
        var notBody = Expression.Not(body);
        var whereClause = Expression.Lambda<Func<TQuery, bool>>(notBody, keySelector.Parameters);
        return queryable.Where(whereClause);
    }

    private static IQueryable<TQuery> ApplyNotContainsAsAFallback<TQuery, TKey>(
        IQueryable<TQuery> queryable,
        Expression<Func<TQuery, TKey>> keySelector,
        TKey[] distinctValues) where TKey : notnull
    {
        var parameter = Expression.Parameter(typeof(TQuery));
        var keySelectorInvoke = Expression.Invoke(keySelector, parameter);
        var containsMethod = ContainsGenericMethod.MakeGenericMethod(typeof(TKey));

        var valuesConstant = Expression.Constant(distinctValues, typeof(TKey[]));
        var containsCall = Expression.Call(null, containsMethod, valuesConstant, keySelectorInvoke);
        var notContainsCall = Expression.Not(containsCall);
        var filterExpression = Expression.Lambda<Func<TQuery, bool>>(notContainsCall, parameter);
        var filteredEntities = queryable.Where(filterExpression);

        return filteredEntities;
    }

    private static IQueryable<TQuery> ApplyContainsAsAFallback<TQuery, TKey>(
        IQueryable<TQuery> queryable,
        Expression<Func<TQuery, TKey>> keySelector,
        TKey[] distinctValues) where TKey : notnull
    {
        var parameter = Expression.Parameter(typeof(TQuery));
        var keySelectorInvoke = Expression.Invoke(keySelector, parameter);
        var containsMethod = ContainsGenericMethod.MakeGenericMethod(typeof(TKey));

        var valuesConstant = Expression.Constant(distinctValues, typeof(TKey[]));
        var containsCall = Expression.Call(null, containsMethod, valuesConstant, keySelectorInvoke);
        var filterExpression = Expression.Lambda<Func<TQuery, bool>>(containsCall, parameter);
        var filteredEntities = queryable.Where(filterExpression);

        return filteredEntities;
    }

    private static BinaryExpression AggregateConditionsIntoOrElseTree(BinaryExpression[] binaryExpressions)
        => AggregateConditions(binaryExpressions, 0, binaryExpressions.Length, Expression.OrElse);

    private static BinaryExpression AggregateConditions(
        BinaryExpression[] binaryExpressions, int from, int to,
        Func<BinaryExpression, BinaryExpression, BinaryExpression> aggregateFunction)
    {
        if (to - from == 0)
        {
            throw new InvalidOperationException("Impossible to generate a binary tree from zero nodes");
        }

        if (to - from == 1)
        {
            return binaryExpressions[from];
        }

        var midpoint = (to + from) / 2;

        return aggregateFunction(
            AggregateConditions(binaryExpressions, from, midpoint, aggregateFunction),
            AggregateConditions(binaryExpressions, midpoint, to, aggregateFunction));
    }
}