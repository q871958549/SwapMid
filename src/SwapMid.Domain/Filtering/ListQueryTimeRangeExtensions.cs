using System;
using System.Linq;
using System.Linq.Expressions;

namespace SwapMid;

public static class ListQueryTimeRangeExtensions
{
    public static IQueryable<TEntity> ApplyTimeRange<TEntity>(
        this IQueryable<TEntity> query,
        DateTime? startTime,
        DateTime? endTime,
        ListQueryTimeType timeType = ListQueryTimeType.CreationTime,
        ListQueryTimeType fallbackTimeType = ListQueryTimeType.CreationTime)
    {
        if (!startTime.HasValue && !endTime.HasValue)
        {
            return query;
        }

        var propertyName = ResolvePropertyName(typeof(TEntity), timeType)
            ?? ResolvePropertyName(typeof(TEntity), fallbackTimeType)
            ?? ResolvePropertyName(typeof(TEntity), ListQueryTimeType.CreationTime);

        if (propertyName == null)
        {
            return query;
        }

        if (startTime.HasValue)
        {
            query = query.Where(BuildPredicate<TEntity>(propertyName, startTime.Value, isStart: true));
        }

        if (endTime.HasValue)
        {
            query = query.Where(BuildPredicate<TEntity>(propertyName, endTime.Value, isStart: false));
        }

        return query;
    }

    private static string? ResolvePropertyName(Type entityType, ListQueryTimeType timeType)
    {
        var propertyName = timeType switch
        {
            ListQueryTimeType.CreationTime => "CreationTime",
            ListQueryTimeType.LastModificationTime => "LastModificationTime",
            ListQueryTimeType.StartedAt => "StartedAt",
            ListQueryTimeType.FinishedAt => "FinishedAt",
            ListQueryTimeType.DeliveredAt => "DeliveredAt",
            _ => "CreationTime"
        };

        var property = entityType.GetProperty(propertyName);
        return property is { PropertyType: var type } && (type == typeof(DateTime) || type == typeof(DateTime?))
            ? propertyName
            : null;
    }

    private static Expression<Func<TEntity, bool>> BuildPredicate<TEntity>(
        string propertyName,
        DateTime value,
        bool isStart)
    {
        var parameter = Expression.Parameter(typeof(TEntity), "x");
        var property = Expression.Property(parameter, propertyName);
        var constant = Expression.Constant(value, typeof(DateTime));

        if (property.Type == typeof(DateTime?))
        {
            var hasValue = Expression.Property(property, nameof(Nullable<DateTime>.HasValue));
            var dateValue = Expression.Property(property, nameof(Nullable<DateTime>.Value));
            var compare = isStart
                ? Expression.GreaterThanOrEqual(dateValue, constant)
                : Expression.LessThanOrEqual(dateValue, constant);

            return Expression.Lambda<Func<TEntity, bool>>(Expression.AndAlso(hasValue, compare), parameter);
        }

        var body = isStart
            ? Expression.GreaterThanOrEqual(property, constant)
            : Expression.LessThanOrEqual(property, constant);

        return Expression.Lambda<Func<TEntity, bool>>(body, parameter);
    }
}
