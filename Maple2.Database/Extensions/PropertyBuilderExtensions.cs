using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Maple2.Database.Extensions;

// The version of the JSON the metadata columns store. The ingest seeds each table's checksum with it (TypeMapper.Process):
// the checksum is of the mapped records, so a change in how a record is stored (PropertyBuilderExtensions.Options)
// with the same records would leave every table "up-to-date" and its rows in the old JSON. Raise it with such a change.
// 2: non-zero constructor defaults are written (the quest rank).
public static class StoredJson {
    public const int Format = 2;
}

internal static class PropertyBuilderExtensions {
    // A member at its type's default is left out, and reading fills a missing constructor parameter with the
    // parameter's own default. A member whose constructor parameter has another default (the quest rank, 5) is
    // always written: left out, a stored 0 read back as that default (rank S quests read as rank none, so their
    // useMainFamePoint gave no fame).
    internal static readonly JsonSerializerOptions Options = new() {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver { Modifiers = { WriteNonZeroParameterDefaults } },
    };

    static PropertyBuilderExtensions() {
        Options.Converters.Add(new Vector3Converter());
        Options.Converters.Add(new SpecialOptionConverter());
    }

    private static void WriteNonZeroParameterDefaults(JsonTypeInfo typeInfo) {
        if (typeInfo.Kind != JsonTypeInfoKind.Object) {
            return;
        }
        ParameterInfo[] parameters = typeInfo.Type.GetConstructors(BindingFlags.Public | BindingFlags.Instance)
            .SelectMany(ctor => ctor.GetParameters())
            .Where(parameter => parameter.HasDefaultValue && parameter.DefaultValue != null && parameter.ParameterType.IsValueType
                                && !parameter.DefaultValue.Equals(Activator.CreateInstance(Nullable.GetUnderlyingType(parameter.ParameterType) ?? parameter.ParameterType)))
            .ToArray();
        if (parameters.Length == 0) {
            return;
        }
        foreach (JsonPropertyInfo property in typeInfo.Properties) {
            if (parameters.Any(parameter => string.Equals(parameter.Name, property.Name, StringComparison.OrdinalIgnoreCase))) {
                property.ShouldSerialize = static (_, _) => true;
            }
        }
    }

    public static PropertyBuilder<TProperty> HasJsonConversion<TProperty>(this PropertyBuilder<TProperty> builder) {
        return builder.HasConversion(
            property => JsonSerializer.Serialize(property, Options),
            value => JsonSerializer.Deserialize<TProperty>(value, Options)!
        ).HasColumnType("json");
    }

    public static ReferenceReferenceBuilder<TEntity, TRelatedEntity> OneToOne<TEntity, TRelatedEntity>(
            this EntityTypeBuilder<TEntity> builder) where TEntity : class where TRelatedEntity : class {
        return builder.HasOne<TRelatedEntity>().WithOne();
    }

    public static ReferenceReferenceBuilder<TEntity, TRelatedEntity> OneToOne<TEntity, TRelatedEntity>(
            this EntityTypeBuilder<TEntity> builder, Expression<Func<TEntity, TRelatedEntity?>>? navigationExpression)
            where TEntity : class where TRelatedEntity : class {
        return builder.HasOne<TRelatedEntity>(navigationExpression).WithOne();
    }
}
