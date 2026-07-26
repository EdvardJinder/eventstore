using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace EventStoreCore;

/// <summary>
/// Provides typed access to an entity and its original/current property values.
/// </summary>
/// <typeparam name="TEntity">The entity type.</typeparam>
public sealed class EntityChange<TEntity>
    where TEntity : class
{
    private readonly EntityEntry<TEntity> _entry;

    internal EntityChange(EntityEntry<TEntity> entry)
    {
        _entry = entry;
    }

    /// <summary>
    /// The tracked entity.
    /// </summary>
    public TEntity Entity => _entry.Entity;

    /// <summary>
    /// Gets the original value of a property.
    /// </summary>
    /// <typeparam name="TProperty">The property value type.</typeparam>
    /// <param name="property">Selects a direct mapped property.</param>
    /// <returns>The property's original value.</returns>
    public TProperty? Original<TProperty>(Expression<Func<TEntity, TProperty>> property)
    {
        return (TProperty?)_entry.Property(GetPropertyName(property)).OriginalValue;
    }

    /// <summary>
    /// Gets the current value of a property.
    /// </summary>
    /// <typeparam name="TProperty">The property value type.</typeparam>
    /// <param name="property">Selects a direct mapped property.</param>
    /// <returns>The property's current value.</returns>
    public TProperty? Current<TProperty>(Expression<Func<TEntity, TProperty>> property)
    {
        return (TProperty?)_entry.Property(GetPropertyName(property)).CurrentValue;
    }

    /// <summary>
    /// Determines whether a property is marked as modified.
    /// </summary>
    /// <typeparam name="TProperty">The property value type.</typeparam>
    /// <param name="property">Selects a direct mapped property.</param>
    /// <returns><see langword="true" /> when EF marks the property as modified.</returns>
    public bool IsModified<TProperty>(Expression<Func<TEntity, TProperty>> property)
    {
        return _entry.Property(GetPropertyName(property)).IsModified;
    }

    private static string GetPropertyName<TProperty>(Expression<Func<TEntity, TProperty>> property)
    {
        ArgumentNullException.ThrowIfNull(property);

        var body = property.Body is UnaryExpression { Operand: MemberExpression converted }
            ? converted
            : property.Body as MemberExpression;

        if (body?.Expression is not ParameterExpression)
        {
            throw new ArgumentException("The expression must select a direct mapped property.", nameof(property));
        }

        return body.Member.Name;
    }
}
