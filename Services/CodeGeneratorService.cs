using MvcDynamicCodeGenerator.Models;
using System.Text;
using System.Linq;
using System.Collections.Generic;
using System.Globalization;
using System;
using Microsoft.EntityFrameworkCore;

namespace MvcDynamicCodeGenerator.Services
{
    public class CodeGeneratorService
    {
        private readonly TextInfo _textInfo;

        public CodeGeneratorService()
        {
            _textInfo = CultureInfo.CurrentCulture.TextInfo;
        }

        public class InverseNavigationInfo
        {
            public string ReferencingTableName { get; set; }
            public string ForeignKeyPropertyName { get; set; }
            public string NavigationPropertyNameInReferencedTable { get; set; }
            public string PrincipalModelName { get; set; } // Changed from PrincipalEntityName
            public string DependentModelName { get; set; } // Changed from DependentEntityName
        }

        public Dictionary<string, List<InverseNavigationInfo>> AnalyzeInverseRelationships(List<TableDefinitionViewModel> tables, NamingConventionOptionsViewModel namingConventions)
        {
            var inverseRelationships = new Dictionary<string, List<InverseNavigationInfo>>();

            foreach (var dependentTableDef in tables)
            {
                if (string.IsNullOrWhiteSpace(dependentTableDef.TableName)) continue;
                // EntityNameSuffix is removed from NamingConventions, so it won't be appended here
                string dependentModelName = ToPascalCase(dependentTableDef.TableName); 

                foreach (var prop in dependentTableDef.Properties)
                {
                    if (prop.IsForeignKey && !string.IsNullOrEmpty(prop.ReferencedTableName))
                    {
                        string principalTableName = prop.ReferencedTableName;
                        string principalModelName = ToPascalCase(principalTableName);

                        if (!inverseRelationships.ContainsKey(principalTableName))
                        {
                            inverseRelationships[principalTableName] = new List<InverseNavigationInfo>();
                        }
                        string collectionPropertyName = Pluralize(ToPascalCase(dependentTableDef.TableName));

                        inverseRelationships[principalTableName].Add(new InverseNavigationInfo
                        {
                            ReferencingTableName = dependentTableDef.TableName,
                            ForeignKeyPropertyName = ToPascalCase(prop.PropertyName),
                            NavigationPropertyNameInReferencedTable = collectionPropertyName,
                            PrincipalModelName = principalModelName,
                            DependentModelName = dependentModelName
                        });
                    }
                }
            }
            return inverseRelationships;
        }

        public string GenerateModelClass( // Changed from GenerateEntityClass
            TableDefinitionViewModel table,
            SchemaGeneratorViewModel globalOptions,
            Dictionary<string, List<InverseNavigationInfo>> allInverseRelationships)
        {
            var sb = new StringBuilder();
            string modelClassName = ToPascalCase(table.TableName); // No suffix
            bool useFluentApiOnly = globalOptions.ConfigurationStyle == CodeGenerationStyle.FluentApiOnly;
            bool useAnnotations = globalOptions.ConfigurationStyle == CodeGenerationStyle.AnnotationsAndFluentApi ||
                                    globalOptions.ConfigurationStyle == CodeGenerationStyle.AnnotationsOnly;
            bool softDeleteEnabledForTable = table.EnableSoftDelete ?? globalOptions.EnableSoftDeleteGlobally;

            sb.AppendLine($"namespace {globalOptions.RootNamespace}.Models"); // Changed Entities to Models
            sb.AppendLine("{");
            sb.AppendLine("    using System;");
            sb.AppendLine("    using System.Collections.Generic;");
            if (useAnnotations)
            {
                sb.AppendLine("    using System.ComponentModel.DataAnnotations;");
                sb.AppendLine("    using System.ComponentModel.DataAnnotations.Schema;");
            }
            sb.AppendLine();
            sb.AppendLine($"    public class {modelClassName}");
            sb.AppendLine("    {");

            List<InverseNavigationInfo> currentTableInverseNavigations = null;
            bool hasInverseNavigations = allInverseRelationships.TryGetValue(table.TableName, out currentTableInverseNavigations);

            bool needsConstructorForCollections = hasInverseNavigations && currentTableInverseNavigations.Any(invNav =>
            {
                string collectionPropertyName = invNav.NavigationPropertyNameInReferencedTable;
                return !table.Properties.Any(p => ToPascalCase(p.PropertyName) == collectionPropertyName) &&
                       !table.Properties.Any(p => !string.IsNullOrEmpty(p.NavigationPropertyName) && ToPascalCase(p.NavigationPropertyName) == collectionPropertyName);
            });

            if (needsConstructorForCollections)
            {
                sb.AppendLine($"        public {modelClassName}()");
                sb.AppendLine("        {");
                if (hasInverseNavigations)
                {
                    foreach (var inverseNav in currentTableInverseNavigations)
                    {
                        string collectionReferencingModelName = ToPascalCase(inverseNav.ReferencingTableName); // No suffix
                        string collectionPropertyName = inverseNav.NavigationPropertyNameInReferencedTable;

                        if (!table.Properties.Any(p => ToPascalCase(p.PropertyName) == collectionPropertyName) &&
                            !table.Properties.Any(p => !string.IsNullOrEmpty(p.NavigationPropertyName) && ToPascalCase(p.NavigationPropertyName) == collectionPropertyName))
                        {
                            sb.AppendLine($"            {collectionPropertyName} = new HashSet<{collectionReferencingModelName}>();");
                        }
                    }
                }
                sb.AppendLine("        }");
                sb.AppendLine();
            }

            foreach (var prop in table.Properties)
            {
                string pascalCasePropertyName = ToPascalCase(prop.PropertyName);
                string csharpDataType = GetCSharpDataType(prop.DataType, prop.IsNullable);

                if (useAnnotations)
                {
                    if (prop.IsPrimaryKey) sb.AppendLine($"        [Key]");
                    if (prop.MaxLength.HasValue && prop.DataType == "string") sb.AppendLine($"        [MaxLength({prop.MaxLength.Value})]");
                    if (prop.MinLength.HasValue && prop.DataType == "string") sb.AppendLine($"        [MinLength({prop.MinLength.Value})]");
                    if ((prop.RangeMin.HasValue || prop.RangeMax.HasValue) && IsNumericType(prop.DataType))
                    {
                        sb.AppendLine($"        [Range({(prop.RangeMin.HasValue ? prop.RangeMin.Value.ToString(CultureInfo.InvariantCulture) : "double.MinValue")}, {(prop.RangeMax.HasValue ? prop.RangeMax.Value.ToString(CultureInfo.InvariantCulture) : "double.MaxValue")})]");
                    }
                    if (!prop.IsNullable && !prop.IsPrimaryKey && prop.DataType == "string" && string.IsNullOrEmpty(prop.ColumnTypeName))
                    {
                        sb.AppendLine($"        [Required]");
                    }
                    if (prop.IsEmailAddress) sb.AppendLine($"        [EmailAddress]");
                    if (prop.IsPhoneNumber) sb.AppendLine($"        [Phone]");
                    if (prop.IsUrl) sb.AppendLine($"        [Url]");
                    if (!string.IsNullOrEmpty(prop.RegexPattern)) sb.AppendLine($"        [RegularExpression(@\"{prop.RegexPattern.Replace("\"", "\"\"")}\")]");
                    if (!string.IsNullOrEmpty(prop.ColumnTypeName) && globalOptions.ConfigurationStyle != CodeGenerationStyle.FluentApiOnly)
                    {
                        sb.AppendLine($"        [Column(TypeName = \"{prop.ColumnTypeName}\")]");
                    }
                    if (prop.IsTimestamp && prop.DataType == "byte[]") sb.AppendLine($"        [Timestamp]");
                    if (prop.IsConcurrencyToken && !(prop.IsTimestamp && prop.DataType == "byte[]")) sb.AppendLine($"        [ConcurrencyCheck]");
                }

                sb.AppendLine($"        public {csharpDataType} {pascalCasePropertyName} {{ get; set; }}");

                if (prop.IsForeignKey && !string.IsNullOrEmpty(prop.ReferencedTableName))
                {
                    string referencedModelNamePascalCase = ToPascalCase(prop.ReferencedTableName); // No suffix
                    string navPropertyName = !string.IsNullOrEmpty(prop.NavigationPropertyName)
                                                ? ToPascalCase(prop.NavigationPropertyName)
                                                : referencedModelNamePascalCase;
                    string navPropertyNullableSuffix = prop.IsNullable ? "?" : "";

                    if (useAnnotations && pascalCasePropertyName != $"{ToPascalCase(prop.ReferencedTableName)}Id" && globalOptions.ConfigurationStyle != CodeGenerationStyle.FluentApiOnly)
                    {
                        sb.AppendLine($"        [ForeignKey(\"{pascalCasePropertyName}\")]");
                    }
                    sb.AppendLine($"        public virtual {referencedModelNamePascalCase}{navPropertyNullableSuffix} {navPropertyName} {{ get; set; }}");
                }
                sb.AppendLine();
            }

            if (hasInverseNavigations)
            {
                foreach (var inverseNav in currentTableInverseNavigations)
                {
                    string collectionReferencingModelName = ToPascalCase(inverseNav.ReferencingTableName); // No suffix
                    string collectionPropertyName = inverseNav.NavigationPropertyNameInReferencedTable;
                    bool nameConflict = table.Properties.Any(p => ToPascalCase(p.PropertyName) == collectionPropertyName) ||
                                        table.Properties.Any(p => !string.IsNullOrEmpty(p.NavigationPropertyName) && ToPascalCase(p.NavigationPropertyName) == collectionPropertyName);

                    if (!nameConflict)
                    {
                        sb.AppendLine($"        public virtual ICollection<{collectionReferencingModelName}> {collectionPropertyName} {{ get; set; }}");
                        sb.AppendLine();
                    }
                    else
                    {
                        sb.AppendLine($"        // INFO: Skipped inverse navigation property '{collectionPropertyName}' due to a potential naming conflict.");
                        sb.AppendLine($"        //       It would reference a collection of '{collectionReferencingModelName}'.");
                        sb.AppendLine($"        //       Consider renaming the conflicting property or manually adding this navigation.");
                        sb.AppendLine();
                    }
                }
            }

            if (softDeleteEnabledForTable)
            {
                sb.AppendLine($"        public bool IsDeleted {{ get; set; }}");
                sb.AppendLine();
            }

            sb.AppendLine("    }");
            sb.AppendLine("}");
            return sb.ToString();
        }

        public string GenerateDbContextClass(List<TableDefinitionViewModel> tables, SchemaGeneratorViewModel globalOptions, Dictionary<string, List<InverseNavigationInfo>> allInverseRelationships)
        {
            var sb = new StringBuilder();
            string dbContextClassName = ToPascalCase(globalOptions.DbContextName);
            bool fluentApiNeeded = globalOptions.ConfigurationStyle == CodeGenerationStyle.FluentApiOnly ||
                                   globalOptions.ConfigurationStyle == CodeGenerationStyle.AnnotationsAndFluentApi ||
                                   tables.Any(t => (t.EnableSoftDelete ?? globalOptions.EnableSoftDeleteGlobally));

            sb.AppendLine($"namespace {globalOptions.RootNamespace}.Data");
            sb.AppendLine("{");
            sb.AppendLine("    using Microsoft.EntityFrameworkCore;");
            sb.AppendLine($"    using {globalOptions.RootNamespace}.Models;"); // Changed Entities to Models
            sb.AppendLine("    using System.Linq;");
            sb.AppendLine();
            sb.AppendLine($"    public class {dbContextClassName} : DbContext");
            sb.AppendLine("    {");

            foreach (var table in tables)
            {
                if (string.IsNullOrWhiteSpace(table.TableName)) continue;
                string modelName = ToPascalCase(table.TableName); // No suffix
                string dbSetName = Pluralize(modelName);
                sb.AppendLine($"        public DbSet<{modelName}> {dbSetName} {{ get; set; }}");
            }
            sb.AppendLine();
            sb.AppendLine($"        public {dbContextClassName}(DbContextOptions<{dbContextClassName}> options) : base(options)");
            sb.AppendLine("        {");
            sb.AppendLine("        }");
            sb.AppendLine();

            if (fluentApiNeeded || tables.Any(t => t.Properties.Any(p => p.IsForeignKey || p.IsPrimaryKey)))
            {
                sb.AppendLine("        protected override void OnModelCreating(ModelBuilder modelBuilder)");
                sb.AppendLine("        {");
                sb.AppendLine("            base.OnModelCreating(modelBuilder);");
                sb.AppendLine();

                foreach (var table in tables)
                {
                    if (string.IsNullOrWhiteSpace(table.TableName)) continue;
                    string modelNamePascalCase = ToPascalCase(table.TableName); // No suffix
                    bool tableSoftDeleteEnabled = table.EnableSoftDelete ?? globalOptions.EnableSoftDeleteGlobally;

                    sb.AppendLine($"            // Configuration for {modelNamePascalCase}");
                    sb.AppendLine($"            modelBuilder.Entity<{modelNamePascalCase}>(entity =>");
                    sb.AppendLine("            {");

                    var primaryKeys = table.Properties.Where(p => p.IsPrimaryKey).ToList();
                    if (primaryKeys.Any())
                    {
                        if (primaryKeys.Count == 1)
                        {
                            sb.AppendLine($"                entity.HasKey(e => e.{ToPascalCase(primaryKeys.First().PropertyName)});");
                        }
                        else
                        {
                            sb.AppendLine($"                entity.HasKey(e => new {{ {string.Join(", ", primaryKeys.Select(p => $"e.{ToPascalCase(p.PropertyName)}"))} }});");
                        }
                    }

                    if (globalOptions.ConfigurationStyle == CodeGenerationStyle.FluentApiOnly ||
                        globalOptions.ConfigurationStyle == CodeGenerationStyle.AnnotationsAndFluentApi)
                    {
                        foreach (var prop in table.Properties.Where(p => !p.IsForeignKey))
                        {
                            string pascalCasePropName = ToPascalCase(prop.PropertyName);
                            var propConfig = new List<string>();
                            if (!prop.IsNullable && prop.DataType == "string" && string.IsNullOrEmpty(prop.ColumnTypeName) && globalOptions.ConfigurationStyle == CodeGenerationStyle.FluentApiOnly)
                            {
                                propConfig.Add(".IsRequired()");
                            }
                            if (prop.MaxLength.HasValue && prop.DataType == "string") propConfig.Add($".HasMaxLength({prop.MaxLength.Value})");
                            if (!string.IsNullOrEmpty(prop.ColumnTypeName)) propConfig.Add($".HasColumnType(\"{prop.ColumnTypeName}\")");
                            if (prop.IsConcurrencyToken && !(prop.IsTimestamp && prop.DataType == "byte[]")) propConfig.Add(".IsConcurrencyToken()");
                            if (prop.IsTimestamp && prop.DataType == "byte[]") propConfig.Add(".IsRowVersion()");

                            if (propConfig.Any())
                            {
                                sb.AppendLine($"                entity.Property(e => e.{pascalCasePropName})");
                                foreach (var conf in propConfig) sb.AppendLine($"                    {conf}");
                                sb.AppendLine("                    ;");
                            }
                        }
                    }

                    foreach (var prop in table.Properties.Where(p => p.IsForeignKey && !string.IsNullOrEmpty(p.ReferencedTableName)))
                    {
                        string navPropertyName = !string.IsNullOrEmpty(prop.NavigationPropertyName)
                                                    ? ToPascalCase(prop.NavigationPropertyName)
                                                    : ToPascalCase(prop.ReferencedTableName); // No Suffix
                        string fkScalarPropertyName = ToPascalCase(prop.PropertyName);
                        string referencedModelPascalCase = ToPascalCase(prop.ReferencedTableName); // No Suffix

                        string inverseCollectionName = Pluralize(modelNamePascalCase);
                        if (allInverseRelationships.TryGetValue(prop.ReferencedTableName, out var principalInverseNavs))
                        {
                            var matchingInverseNav = principalInverseNavs.FirstOrDefault(
                                inv => inv.ReferencingTableName == table.TableName &&
                                       inv.ForeignKeyPropertyName == fkScalarPropertyName);
                            if (matchingInverseNav != null) inverseCollectionName = matchingInverseNav.NavigationPropertyNameInReferencedTable;
                        }

                        sb.Append($"                entity.HasOne(e => e.{navPropertyName})");
                        var principalTableDef = tables.FirstOrDefault(t => t.TableName == prop.ReferencedTableName);
                        bool hasMatchingInverseNavOnPrincipal = false;
                        if (principalTableDef != null && allInverseRelationships.TryGetValue(prop.ReferencedTableName, out var invNavsForPrincipal))
                        {
                            var specificInverseNav = invNavsForPrincipal.FirstOrDefault(inv =>
                                inv.ReferencingTableName == table.TableName &&
                                ToPascalCase(inv.ForeignKeyPropertyName) == fkScalarPropertyName);
                            if (specificInverseNav != null)
                            {
                                bool conflictOnPrincipal = principalTableDef.Properties.Any(p => ToPascalCase(p.PropertyName) == specificInverseNav.NavigationPropertyNameInReferencedTable) ||
                                                       principalTableDef.Properties.Any(p => !string.IsNullOrEmpty(p.NavigationPropertyName) && ToPascalCase(p.NavigationPropertyName) == specificInverseNav.NavigationPropertyNameInReferencedTable);
                                if (!conflictOnPrincipal)
                                {
                                    hasMatchingInverseNavOnPrincipal = true;
                                    inverseCollectionName = specificInverseNav.NavigationPropertyNameInReferencedTable;
                                }
                            }
                        }

                        if (hasMatchingInverseNavOnPrincipal) sb.Append($"\n                    .WithMany(p => p.{inverseCollectionName})");
                        else sb.Append($"\n                    .WithMany()");
                        
                        sb.Append($"\n                    .HasForeignKey(e => e.{fkScalarPropertyName})");

                        if (!string.IsNullOrEmpty(prop.DeleteBehavior) && Enum.TryParse<DeleteBehavior>(prop.DeleteBehavior, true, out var deleteBehavior))
                        {
                            sb.Append($"\n                    .OnDelete(DeleteBehavior.{deleteBehavior})");
                        }
                        else
                        {
                            if (!prop.IsNullable) sb.Append($"\n                    .OnDelete(DeleteBehavior.Cascade)");
                            else sb.Append($"\n                    .OnDelete(DeleteBehavior.ClientSetNull)");
                        }
                        if (!string.IsNullOrEmpty(prop.CustomFKConstraintName)) sb.Append($"\n                    .HasConstraintName(\"{prop.CustomFKConstraintName}\")");
                        sb.AppendLine(";");
                    }

                    if (tableSoftDeleteEnabled)
                    {
                        sb.AppendLine($"                entity.Property<bool>(\"IsDeleted\");");
                        sb.AppendLine($"                entity.HasQueryFilter(e => !e.IsDeleted);");
                    }
                    sb.AppendLine("            });");
                    sb.AppendLine();
                }
                sb.AppendLine("        }");
            }
            sb.AppendLine("    }");
            sb.AppendLine("}");
            return sb.ToString();
        }

        public string GenerateGenericIRepository(SchemaGeneratorViewModel globalOptions)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"namespace {globalOptions.RootNamespace}.Repositories.Interfaces");
            sb.AppendLine("{");
            sb.AppendLine("    using System;");
            sb.AppendLine("    using System.Linq.Expressions;");
            sb.AppendLine("    using System.Collections.Generic;");
            sb.AppendLine("    using System.Threading.Tasks;");
            sb.AppendLine();
            sb.AppendLine("    public interface IRepository<TModel> where TModel : class"); // Changed TEntity to TModel
            sb.AppendLine("    {");
            sb.AppendLine("        Task<TModel?> GetByIdAsync(object id);");
            sb.AppendLine("        Task<IEnumerable<TModel>> GetAllAsync(bool includeSoftDeleted = false);");
            sb.AppendLine("        Task<IEnumerable<TModel>> FindAsync(Expression<Func<TModel, bool>> predicate, bool includeSoftDeleted = false);");
            sb.AppendLine("        Task AddAsync(TModel model);");
            sb.AppendLine("        Task AddRangeAsync(IEnumerable<TModel> models);");
            sb.AppendLine("        void Update(TModel model);");
            sb.AppendLine("        void Remove(TModel model, bool hardDelete = false);");
            sb.AppendLine("        void RemoveRange(IEnumerable<TModel> models, bool hardDelete = false);");
            sb.AppendLine("        Task<int> CountAsync(Expression<Func<TModel, bool>>? predicate = null, bool includeSoftDeleted = false);");
            sb.AppendLine("        Task<bool> ExistsAsync(Expression<Func<TModel, bool>> predicate, bool includeSoftDeleted = false);");
            sb.AppendLine("    }");
            sb.AppendLine("}");
            return sb.ToString();
        }

        public string GenerateGenericRepositoryClass(SchemaGeneratorViewModel globalOptions)
        {
            var sb = new StringBuilder();
            string dbContextName = ToPascalCase(globalOptions.DbContextName);

            sb.AppendLine($"namespace {globalOptions.RootNamespace}.Repositories.Implementations");
            sb.AppendLine("{");
            sb.AppendLine("    using Microsoft.EntityFrameworkCore;");
            sb.AppendLine("    using System;");
            sb.AppendLine("    using System.Linq.Expressions;");
            sb.AppendLine("    using System.Collections.Generic;");
            sb.AppendLine("    using System.Linq;");
            sb.AppendLine("    using System.Threading.Tasks;");
            sb.AppendLine($"    using {globalOptions.RootNamespace}.Data;");
            sb.AppendLine($"    using {globalOptions.RootNamespace}.Repositories.Interfaces;");
            // sb.AppendLine($"    using {globalOptions.RootNamespace}.Models;"); // Not strictly needed if HasSoftDelete checks property via reflection
            sb.AppendLine();
            sb.AppendLine("    public class Repository<TModel> : IRepository<TModel> where TModel : class"); // Changed TEntity to TModel
            sb.AppendLine("    {");
            sb.AppendLine($"        protected readonly {dbContextName} _context;");
            sb.AppendLine($"        protected readonly DbSet<TModel> _dbSet;");
            sb.AppendLine();
            sb.AppendLine($"        public Repository({dbContextName} context)");
            sb.AppendLine("        {");
            sb.AppendLine("            _context = context ?? throw new ArgumentNullException(nameof(context));");
            sb.AppendLine("            _dbSet = _context.Set<TModel>();");
            sb.AppendLine("        }");
            sb.AppendLine();
            sb.AppendLine("        public virtual async Task<TModel?> GetByIdAsync(object id)");
            sb.AppendLine("        {");
            sb.AppendLine("            var model = await _dbSet.FindAsync(id);");
            sb.AppendLine("            if (model != null && HasSoftDelete(model))");
            sb.AppendLine("            {");
            sb.AppendLine("                bool isDeleted = false;");
            sb.AppendLine("                try { isDeleted = EF.Property<bool>(model, \"IsDeleted\"); }");
            sb.AppendLine("                catch (InvalidOperationException) { /* Property does not exist or not mapped */ }");
            sb.AppendLine("                if (isDeleted) return null;");
            sb.AppendLine("            }");
            sb.AppendLine("            return model;"); // Corrected variable name
            sb.AppendLine("        }"); // Added missing closing brace
            sb.AppendLine();
            sb.AppendLine("        public virtual async Task<IEnumerable<TModel>> GetAllAsync(bool includeSoftDeleted = false)");
            sb.AppendLine("        {");
            sb.AppendLine("            IQueryable<TModel> query = _dbSet;");
            sb.AppendLine("            if (includeSoftDeleted && typeof(TModel).GetProperty(\"IsDeleted\") != null)");
            sb.AppendLine("            {");
            sb.AppendLine("                query = query.IgnoreQueryFilters();");
            sb.AppendLine("            }");
            sb.AppendLine("            return await query.ToListAsync();");
            sb.AppendLine("        }");
            sb.AppendLine();
            sb.AppendLine("        public virtual async Task<IEnumerable<TModel>> FindAsync(Expression<Func<TModel, bool>> predicate, bool includeSoftDeleted = false)");
            sb.AppendLine("        {");
            sb.AppendLine("            IQueryable<TModel> query = _dbSet;");
            sb.AppendLine("            if (includeSoftDeleted && typeof(TModel).GetProperty(\"IsDeleted\") != null)");
            sb.AppendLine("            {");
            sb.AppendLine("                query = query.IgnoreQueryFilters();");
            sb.AppendLine("            }");
            sb.AppendLine("            query = query.Where(predicate);");
            sb.AppendLine("            return await query.ToListAsync();");
            sb.AppendLine("        }");
            sb.AppendLine();
            sb.AppendLine("        public virtual async Task AddAsync(TModel model)");
            sb.AppendLine("        {");
            sb.AppendLine("            await _dbSet.AddAsync(model);");
            sb.AppendLine("        }");
            sb.AppendLine();
            sb.AppendLine("        public virtual async Task AddRangeAsync(IEnumerable<TModel> models)");
            sb.AppendLine("        {");
            sb.AppendLine("            await _dbSet.AddRangeAsync(models);");
            sb.AppendLine("        }");
            sb.AppendLine();
            sb.AppendLine("        public virtual void Update(TModel model)");
            sb.AppendLine("        {");
            sb.AppendLine("            _dbSet.Attach(model);");
            sb.AppendLine("            _context.Entry(model).State = EntityState.Modified;");
            sb.AppendLine("        }");
            sb.AppendLine();
            sb.AppendLine("        public virtual void Remove(TModel model, bool hardDelete = false)");
            sb.AppendLine("        {");
            sb.AppendLine("            if (!hardDelete && HasSoftDelete(model))");
            sb.AppendLine("            {");
            sb.AppendLine("                _context.Entry(model).Property(\"IsDeleted\").CurrentValue = true;");
            sb.AppendLine("                _context.Entry(model).State = EntityState.Modified;");
            sb.AppendLine("            }");
            sb.AppendLine("            else");
            sb.AppendLine("            {");
            sb.AppendLine("                _dbSet.Remove(model);");
            sb.AppendLine("            }");
            sb.AppendLine("        }");
            sb.AppendLine();
            sb.AppendLine("        public virtual void RemoveRange(IEnumerable<TModel> models, bool hardDelete = false)");
            sb.AppendLine("        {");
            sb.AppendLine("            if (!hardDelete && models.Any() && HasSoftDelete(models.First()))"); // Check if any model and if type supports soft delete
            sb.AppendLine("            {");
            sb.AppendLine("                foreach (var model in models)");
            sb.AppendLine("                {");
            sb.AppendLine("                    _context.Entry(model).Property(\"IsDeleted\").CurrentValue = true;");
            sb.AppendLine("                    _context.Entry(model).State = EntityState.Modified;");
            sb.AppendLine("                }");
            sb.AppendLine("            }");
            sb.AppendLine("            else");
            sb.AppendLine("            {");
            sb.AppendLine("                _dbSet.RemoveRange(models);");
            sb.AppendLine("            }");
            sb.AppendLine("        }");
            sb.AppendLine();
            sb.AppendLine("        public virtual async Task<int> CountAsync(Expression<Func<TModel, bool>>? predicate = null, bool includeSoftDeleted = false)");
            sb.AppendLine("        {");
            sb.AppendLine("            IQueryable<TModel> query = _dbSet;");
            sb.AppendLine("            if (includeSoftDeleted && typeof(TModel).GetProperty(\"IsDeleted\") != null)");
            sb.AppendLine("            {");
            sb.AppendLine("                query = query.IgnoreQueryFilters();");
            sb.AppendLine("            }");
            sb.AppendLine("            if (predicate == null) return await query.CountAsync();");
            sb.AppendLine("            return await query.CountAsync(predicate);");
            sb.AppendLine("        }");
            sb.AppendLine();
            sb.AppendLine("        public virtual async Task<bool> ExistsAsync(Expression<Func<TModel, bool>> predicate, bool includeSoftDeleted = false)");
            sb.AppendLine("        {");
            sb.AppendLine("            IQueryable<TModel> query = _dbSet;");
            sb.AppendLine("            if (includeSoftDeleted && typeof(TModel).GetProperty(\"IsDeleted\") != null)");
            sb.AppendLine("            {");
            sb.AppendLine("                query = query.IgnoreQueryFilters();");
            sb.AppendLine("            }");
            sb.AppendLine("            return await query.AnyAsync(predicate);");
            sb.AppendLine("        }");
            sb.AppendLine();
            sb.AppendLine("        private bool HasSoftDelete(TModel model)");
            sb.AppendLine("        {");
            sb.AppendLine("            return model.GetType().GetProperty(\"IsDeleted\") != null;");
            sb.AppendLine("        }");
            sb.AppendLine("    }");
            sb.AppendLine("}");
            return sb.ToString();
        }

        public string GenerateSpecificIRepository(TableDefinitionViewModel table, SchemaGeneratorViewModel globalOptions)
        {
            var sb = new StringBuilder();
            string modelClassName = ToPascalCase(table.TableName); // No suffix
            string iRepositoryName = $"{globalOptions.NamingConventions.RepositoryInterfacePrefix}{modelClassName}{globalOptions.NamingConventions.RepositoryClassSuffix}";

            sb.AppendLine($"namespace {globalOptions.RootNamespace}.Repositories.Interfaces");
            sb.AppendLine("{");
            sb.AppendLine($"    using {globalOptions.RootNamespace}.Models;"); // Changed Entities to Models
            sb.AppendLine();
            sb.AppendLine($"    public interface {iRepositoryName} : IRepository<{modelClassName}>");
            sb.AppendLine("    {");
            sb.AppendLine("        // Add model-specific method signatures here if needed");
            sb.AppendLine($"        // Example: Task<IEnumerable<{modelClassName}>> GetActive{Pluralize(modelClassName)}Async();");
            sb.AppendLine("    }");
            sb.AppendLine("}");
            return sb.ToString();
        }

        public string GenerateSpecificRepositoryClass(TableDefinitionViewModel table, SchemaGeneratorViewModel globalOptions)
        {
            var sb = new StringBuilder();
            string modelClassName = ToPascalCase(table.TableName); // No suffix
            string dbContextName = ToPascalCase(globalOptions.DbContextName);
            string repositoryName = $"{modelClassName}{globalOptions.NamingConventions.RepositoryClassSuffix}";
            string iRepositoryName = $"{globalOptions.NamingConventions.RepositoryInterfacePrefix}{modelClassName}{globalOptions.NamingConventions.RepositoryClassSuffix}";

            sb.AppendLine($"namespace {globalOptions.RootNamespace}.Repositories.Implementations");
            sb.AppendLine("{");
            sb.AppendLine($"    using {globalOptions.RootNamespace}.Data;");
            sb.AppendLine($"    using {globalOptions.RootNamespace}.Models;"); // Changed Entities to Models
            sb.AppendLine($"    using {globalOptions.RootNamespace}.Repositories.Interfaces;");
            sb.AppendLine("    using Microsoft.EntityFrameworkCore;");
            sb.AppendLine("    using System.Collections.Generic;");
            sb.AppendLine("    using System.Linq;");
            sb.AppendLine("    using System.Threading.Tasks;");
            sb.AppendLine();
            sb.AppendLine($"    public class {repositoryName} : Repository<{modelClassName}>, {iRepositoryName}");
            sb.AppendLine("    {");
            sb.AppendLine($"        public {repositoryName}({dbContextName} context) : base(context)");
            sb.AppendLine("        {");
            sb.AppendLine("        }");
            sb.AppendLine();
            sb.AppendLine("        // Implement model-specific methods here if defined in the interface");
            sb.AppendLine("    }");
            sb.AppendLine("}");
            return sb.ToString();
        }

        public string GenerateIServiceInterface(TableDefinitionViewModel table, SchemaGeneratorViewModel globalOptions)
        {
            var sb = new StringBuilder();
            string modelClassName = ToPascalCase(table.TableName); // No suffix
            string iServiceName = $"{globalOptions.NamingConventions.ServiceInterfacePrefix}{modelClassName}{globalOptions.NamingConventions.ServiceClassSuffix}";
            string pkDataType = GetPrimaryKeyDataType(table);
            string pkPropertyName = GetPrimaryKeyPropertyName(table);

            sb.AppendLine($"namespace {globalOptions.RootNamespace}.Services.Interfaces");
            sb.AppendLine("{");
            sb.AppendLine($"    using {globalOptions.RootNamespace}.Models;"); // Services will use Models directly
            sb.AppendLine("    using System.Collections.Generic;");
            sb.AppendLine("    using System.Threading.Tasks;");
            sb.AppendLine();
            sb.AppendLine($"    public interface {iServiceName}");
            sb.AppendLine("    {");
            if (globalOptions.AsyncServiceOnly)
            {
                sb.AppendLine($"        Task<{modelClassName}?> GetByIdAsync({pkDataType} {ToCamelCase(pkPropertyName)});");
                sb.AppendLine($"        Task<IEnumerable<{modelClassName}>> GetAllAsync(bool includeSoftDeleted = false);");
                sb.AppendLine($"        Task<{modelClassName}> CreateAsync({modelClassName} model);"); // Takes Model
                sb.AppendLine($"        Task UpdateAsync({pkDataType} {ToCamelCase(pkPropertyName)}, {modelClassName} model);"); // Takes Model
                sb.AppendLine($"        Task DeleteAsync({pkDataType} {ToCamelCase(pkPropertyName)}, bool hardDelete = false);");
            }
            else
            {
                sb.AppendLine($"        {modelClassName}? GetById({pkDataType} {ToCamelCase(pkPropertyName)});");
                sb.AppendLine($"        IEnumerable<{modelClassName}> GetAll(bool includeSoftDeleted = false);");
                sb.AppendLine($"        {modelClassName} Create({modelClassName} model);");
                sb.AppendLine($"        void Update({pkDataType} {ToCamelCase(pkPropertyName)}, {modelClassName} model);");
                sb.AppendLine($"        void Delete({pkDataType} {ToCamelCase(pkPropertyName)}, bool hardDelete = false);");

                sb.AppendLine($"        Task<{modelClassName}?> GetByIdAsync({pkDataType} {ToCamelCase(pkPropertyName)});");
                sb.AppendLine($"        Task<IEnumerable<{modelClassName}>> GetAllAsync(bool includeSoftDeleted = false);");
                sb.AppendLine($"        Task<{modelClassName}> CreateAsync({modelClassName} model);");
                sb.AppendLine($"        Task UpdateAsync({pkDataType} {ToCamelCase(pkPropertyName)}, {modelClassName} model);");
                sb.AppendLine($"        Task DeleteAsync({pkDataType} {ToCamelCase(pkPropertyName)}, bool hardDelete = false);");
            }
            sb.AppendLine("    }");
            sb.AppendLine("}");
            return sb.ToString();
        }

        public string GenerateServiceClass(TableDefinitionViewModel table, SchemaGeneratorViewModel globalOptions)
        {
            var sb = new StringBuilder();
            string modelClassName = ToPascalCase(table.TableName); // No suffix
            string dbContextName = ToPascalCase(globalOptions.DbContextName);
            string repositoryInterfaceName = $"{globalOptions.NamingConventions.RepositoryInterfacePrefix}{modelClassName}{globalOptions.NamingConventions.RepositoryClassSuffix}";
            string serviceName = $"{modelClassName}{globalOptions.NamingConventions.ServiceClassSuffix}";
            string iServiceName = $"{globalOptions.NamingConventions.ServiceInterfacePrefix}{modelClassName}{globalOptions.NamingConventions.ServiceClassSuffix}";
            string pkDataType = GetPrimaryKeyDataType(table);
            string pkPropertyName = GetPrimaryKeyPropertyName(table);
            string pkPropertyNameCamel = ToCamelCase(pkPropertyName);

            sb.AppendLine($"namespace {globalOptions.RootNamespace}.Services.Implementations");
            sb.AppendLine("{");
            sb.AppendLine($"    using {globalOptions.RootNamespace}.Models;"); // Using Models
            sb.AppendLine($"    using {globalOptions.RootNamespace}.Repositories.Interfaces;");
            sb.AppendLine($"    using {globalOptions.RootNamespace}.Services.Interfaces;");
            sb.AppendLine($"    using {globalOptions.RootNamespace}.Data;");
            sb.AppendLine("    using System;");
            sb.AppendLine("    using System.Collections.Generic;");
            sb.AppendLine("    using System.Linq;"); // Keep for potential future use
            sb.AppendLine("    using System.Threading.Tasks;");
            sb.AppendLine();
            sb.AppendLine($"    public class {serviceName} : {iServiceName}");
            sb.AppendLine("    {");
            sb.AppendLine($"        private readonly {repositoryInterfaceName} _{ToCamelCase(modelClassName)}Repository;");
            sb.AppendLine($"        private readonly {dbContextName} _dbContext;");
            sb.AppendLine();
            sb.AppendLine($"        public {serviceName}({repositoryInterfaceName} {ToCamelCase(modelClassName)}Repository, {dbContextName} dbContext)");
            sb.AppendLine("        {");
            sb.AppendLine($"            _{ToCamelCase(modelClassName)}Repository = {ToCamelCase(modelClassName)}Repository ?? throw new ArgumentNullException(nameof({ToCamelCase(modelClassName)}Repository));");
            sb.AppendLine($"            _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));");
            sb.AppendLine("        }");
            sb.AppendLine();

            sb.AppendLine($"        public async Task<{modelClassName}?> GetByIdAsync({pkDataType} {pkPropertyNameCamel})");
            sb.AppendLine("        {");
            sb.AppendLine($"            return await _{ToCamelCase(modelClassName)}Repository.GetByIdAsync({pkPropertyNameCamel});");
            sb.AppendLine("        }");
            sb.AppendLine();

            sb.AppendLine($"        public async Task<IEnumerable<{modelClassName}>> GetAllAsync(bool includeSoftDeleted = false)");
            sb.AppendLine("        {");
            sb.AppendLine($"            return await _{ToCamelCase(modelClassName)}Repository.GetAllAsync(includeSoftDeleted);");
            sb.AppendLine("        }");
            sb.AppendLine();

            sb.AppendLine($"        public async Task<{modelClassName}> CreateAsync({modelClassName} model)");
            sb.AppendLine("        {");
            sb.AppendLine($"            if (model == null) throw new ArgumentNullException(nameof(model));");
            sb.AppendLine($"            await _{ToCamelCase(modelClassName)}Repository.AddAsync(model);");
            sb.AppendLine("            await _dbContext.SaveChangesAsync();");
            sb.AppendLine("            return model;");
            sb.AppendLine("        }");
            sb.AppendLine();

            sb.AppendLine($"        public async Task UpdateAsync({pkDataType} {pkPropertyNameCamel}, {modelClassName} model)");
            sb.AppendLine("        {");
            sb.AppendLine($"            if (model == null) throw new ArgumentNullException(nameof(model));");
            // Ensure the ID from the path matches the ID in the model, or set it.
            // A common practice is to fetch the existing entity first.
            sb.AppendLine($"            var existingModel = await _{ToCamelCase(modelClassName)}Repository.GetByIdAsync({pkPropertyNameCamel});");
            sb.AppendLine($"            if (existingModel == null) throw new KeyNotFoundException($\"'{modelClassName}' with ID '{{{pkPropertyNameCamel}}}' not found.\");");
            sb.AppendLine();
            // Update properties of existingModel from model (manual or AutoMapper)
            // For simplicity, assuming direct update after fetch if ID matches or is set correctly.
            // This part might need more sophisticated mapping in a real app.
              sb.AppendLine("     _context.Entry(existingModel).CurrentValues.SetValues(model);     }");
           // Simple update
            // If you don't fetch first, you'd do:
            // _context.Entry(model).Property(x => x.Id).CurrentValue = id; // Ensure PK is set if not part of model body
            // _{ToCamelCase(modelClassName)}Repository.Update(model);
            sb.AppendLine("            await _dbContext.SaveChangesAsync();");
            sb.AppendLine("        }");
            sb.AppendLine();

            sb.AppendLine($"        public async Task DeleteAsync({pkDataType} {pkPropertyNameCamel}, bool hardDelete = false)");
            sb.AppendLine("        {");
            sb.AppendLine($"            var model = await _{ToCamelCase(modelClassName)}Repository.GetByIdAsync({pkPropertyNameCamel});");
            sb.AppendLine($"            if (model == null) throw new KeyNotFoundException($\"'{modelClassName}' with ID '{{{pkPropertyNameCamel}}}' not found.\");");
            sb.AppendLine();
            sb.AppendLine($"            _{ToCamelCase(modelClassName)}Repository.Remove(model, hardDelete);");
            sb.AppendLine("            await _dbContext.SaveChangesAsync();");
            sb.AppendLine("        }");
            sb.AppendLine();

            if (!globalOptions.AsyncServiceOnly)
            {
                sb.AppendLine($"        public {modelClassName}? GetById({pkDataType} {pkPropertyNameCamel}) => GetByIdAsync({pkPropertyNameCamel}).GetAwaiter().GetResult();");
                sb.AppendLine($"        public IEnumerable<{modelClassName}> GetAll(bool includeSoftDeleted = false) => GetAllAsync(includeSoftDeleted).GetAwaiter().GetResult();");
                sb.AppendLine($"        public {modelClassName} Create({modelClassName} model) => CreateAsync(model).GetAwaiter().GetResult();");
                sb.AppendLine($"        public void Update({pkDataType} {pkPropertyNameCamel}, {modelClassName} model) => UpdateAsync({pkPropertyNameCamel}, model).GetAwaiter().GetResult();");
                sb.AppendLine($"        public void Delete({pkDataType} {pkPropertyNameCamel}, bool hardDelete = false) => DeleteAsync({pkPropertyNameCamel}, hardDelete).GetAwaiter().GetResult();");
                sb.AppendLine();
            }
            sb.AppendLine("    }");
            sb.AppendLine("}");
            return sb.ToString();
        }

        // DTO Generation methods are removed.

        public string GenerateRepositoryUnitTests(TableDefinitionViewModel table, SchemaGeneratorViewModel globalOptions)
        {
            var sb = new StringBuilder();
            string modelClassName = ToPascalCase(table.TableName); // No suffix
            string dbContextName = ToPascalCase(globalOptions.DbContextName);
            string repositoryClassName = $"{modelClassName}{globalOptions.NamingConventions.RepositoryClassSuffix}";
            string repositoryInterfaceName = $"{globalOptions.NamingConventions.RepositoryInterfacePrefix}{modelClassName}{globalOptions.NamingConventions.RepositoryClassSuffix}";
            string testClassName = $"{repositoryClassName}Tests";

            sb.AppendLine($"// File: Tests/{globalOptions.RootNamespace}.Tests/Repositories/{testClassName}.cs");
            sb.AppendLine($"namespace {globalOptions.RootNamespace}.Tests.Repositories");
            sb.AppendLine("{");
            sb.AppendLine("    using Microsoft.EntityFrameworkCore;");
            sb.AppendLine($"    using {globalOptions.RootNamespace}.Data;");
            sb.AppendLine($"    using {globalOptions.RootNamespace}.Models;"); // Changed to Models
            sb.AppendLine($"    using {globalOptions.RootNamespace}.Repositories.Implementations;");
            sb.AppendLine($"    using {globalOptions.RootNamespace}.Repositories.Interfaces;");
            sb.AppendLine("    using System;");
            sb.AppendLine("    using System.Linq;");
            sb.AppendLine("    using System.Threading.Tasks;");
            sb.AppendLine("    using Xunit;");
            sb.AppendLine();
            sb.AppendLine($"    public class {testClassName} : IDisposable");
            sb.AppendLine("    {");
            sb.AppendLine($"        private readonly {dbContextName} _context;");
            sb.AppendLine($"        private readonly {repositoryInterfaceName} _{ToCamelCase(repositoryClassName)};");
            sb.AppendLine();
            sb.AppendLine($"        public {testClassName}()");
            sb.AppendLine("        {");
            sb.AppendLine($"            var options = new DbContextOptionsBuilder<{dbContextName}>()");
            sb.AppendLine($"                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())");
            sb.AppendLine("                .Options;");
            sb.AppendLine($"            _context = new {dbContextName}(options);");
            sb.AppendLine($"            _{ToCamelCase(repositoryClassName)} = new {repositoryClassName}(_context);");
            sb.AppendLine("        }");
            sb.AppendLine();
            sb.AppendLine("        [Fact]");
            sb.AppendLine($"        public async Task GetByIdAsync_ShouldReturn{modelClassName}_WhenExists()");
            sb.AppendLine("        {");
            sb.AppendLine($"            var testModel = new {modelClassName} {{ {GetPrimaryKeyPropertyName(table)} = ({GetPrimaryKeyDataType(table)})Convert.ChangeType(1, typeof({GetPrimaryKeyDataType(table)})) }};");
            sb.AppendLine($"            _context.{Pluralize(modelClassName)}.Add(testModel);");
            sb.AppendLine("            await _context.SaveChangesAsync();");
            sb.AppendLine($"            var result = await _{ToCamelCase(repositoryClassName)}.GetByIdAsync(testModel.{GetPrimaryKeyPropertyName(table)});");
            sb.AppendLine("            Assert.NotNull(result);");
            sb.AppendLine($"            Assert.Equal(testModel.{GetPrimaryKeyPropertyName(table)}, result.{GetPrimaryKeyPropertyName(table)});");
            sb.AppendLine("        }");
            sb.AppendLine();
            sb.AppendLine("        public void Dispose()");
            sb.AppendLine("        {");
            sb.AppendLine("            _context.Database.EnsureDeleted();");
            sb.AppendLine("            _context.Dispose();");
            sb.AppendLine("        }");
            sb.AppendLine("    }");
            sb.AppendLine("}");
            return sb.ToString();
        }

        public string GenerateServiceUnitTests(TableDefinitionViewModel table, SchemaGeneratorViewModel globalOptions)
        {
            var sb = new StringBuilder();
            string modelClassName = ToPascalCase(table.TableName); // No suffix
            string dbContextName = ToPascalCase(globalOptions.DbContextName);
            string serviceClassName = $"{modelClassName}{globalOptions.NamingConventions.ServiceClassSuffix}";
            string serviceInterfaceName = $"{globalOptions.NamingConventions.ServiceInterfacePrefix}{modelClassName}{globalOptions.NamingConventions.ServiceClassSuffix}";
            string repositoryInterfaceName = $"{globalOptions.NamingConventions.RepositoryInterfacePrefix}{modelClassName}{globalOptions.NamingConventions.RepositoryClassSuffix}";
            string testClassName = $"{serviceClassName}Tests";
            string pkDataType = GetPrimaryKeyDataType(table);
            string pkPropertyName = GetPrimaryKeyPropertyName(table);
            string pkPropertyNameCamel = ToCamelCase(pkPropertyName);

            sb.AppendLine($"// File: Tests/{globalOptions.RootNamespace}.Tests/Services/{testClassName}.cs");
            sb.AppendLine($"namespace {globalOptions.RootNamespace}.Tests.Services");
            sb.AppendLine("{");
            sb.AppendLine($"    using {globalOptions.RootNamespace}.Services.Implementations;");
            sb.AppendLine($"    using {globalOptions.RootNamespace}.Services.Interfaces;");
            sb.AppendLine($"    using {globalOptions.RootNamespace}.Repositories.Interfaces;");
            sb.AppendLine($"    using {globalOptions.RootNamespace}.Models;"); // Using Models
            sb.AppendLine($"    using {globalOptions.RootNamespace}.Data;");
            sb.AppendLine("    using Moq;");
            sb.AppendLine("    using System;");
            sb.AppendLine("    using System.Threading.Tasks;");
            sb.AppendLine("    using Xunit;");
            sb.AppendLine("    using Microsoft.EntityFrameworkCore;");
            sb.AppendLine();
            sb.AppendLine($"    public class {testClassName}");
            sb.AppendLine("    {");
            sb.AppendLine($"        private readonly Mock<{repositoryInterfaceName}> _mock{modelClassName}Repository;");
            sb.AppendLine($"        private readonly Mock<{dbContextName}> _mockDbContext;");
            sb.AppendLine($"        private readonly {serviceInterfaceName} _{ToCamelCase(serviceClassName)};");
            sb.AppendLine();
            sb.AppendLine($"        public {testClassName}()");
            sb.AppendLine("        {");
            sb.AppendLine($"            _mock{modelClassName}Repository = new Mock<{repositoryInterfaceName}>();");
            sb.AppendLine($"            var dbContextOptions = new DbContextOptionsBuilder<{dbContextName}>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;");
            sb.AppendLine($"            _mockDbContext = new Mock<{dbContextName}>(dbContextOptions);");
            sb.AppendLine($"            _mockDbContext.Setup(db => db.SaveChangesAsync(It.IsAny<System.Threading.CancellationToken>())).ReturnsAsync(1);");
            sb.AppendLine($"            _{ToCamelCase(serviceClassName)} = new {serviceClassName}(_mock{modelClassName}Repository.Object, _mockDbContext.Object);");
            sb.AppendLine("        }");
            sb.AppendLine();
            sb.AppendLine("        [Fact]");
            sb.AppendLine($"        public async Task GetByIdAsync_ShouldReturn{modelClassName}_When{modelClassName}Exists()");
            sb.AppendLine("        {");
            sb.AppendLine($"            var {pkPropertyNameCamel} = ({pkDataType})Convert.ChangeType(1, typeof({pkDataType}));");
            sb.AppendLine($"            var mockModel = new {modelClassName} {{ {pkPropertyName} = {pkPropertyNameCamel} }};");
            sb.AppendLine($"            _mock{modelClassName}Repository.Setup(repo => repo.GetByIdAsync({pkPropertyNameCamel})).ReturnsAsync(mockModel);");
            sb.AppendLine($"            var result = await _{ToCamelCase(serviceClassName)}.GetByIdAsync({pkPropertyNameCamel});");
            sb.AppendLine("            Assert.NotNull(result);");
            sb.AppendLine($"            Assert.Equal(mockModel.{pkPropertyName}, result.{pkPropertyName});");
            sb.AppendLine($"            _mock{modelClassName}Repository.Verify(repo => repo.GetByIdAsync({pkPropertyNameCamel}), Times.Once);");
            sb.AppendLine("        }");
            sb.AppendLine();
            sb.AppendLine("        [Fact]");
            sb.AppendLine($"        public async Task CreateAsync_ShouldCallAddAndSaveChanges_AndReturnModel()");
            sb.AppendLine("        {");
            sb.AppendLine($"            var modelToCreate = new {modelClassName} {{ /* Initialize properties needed for creation, excluding PK if auto-generated */ }};");
            sb.AppendLine($"            var createdModel = new {modelClassName} {{ {pkPropertyName} = ({pkDataType})Convert.ChangeType(1, typeof({pkDataType})) /*, other props from modelToCreate */ }};");
            sb.AppendLine($"            _mock{modelClassName}Repository.Setup(repo => repo.AddAsync(It.IsAny<{modelClassName}>()))");
            sb.AppendLine($"                 .Callback<{modelClassName}>(m => {{ m.{pkPropertyName} = createdModel.{pkPropertyName}; }});"); // Simulate DB setting PK
            sb.AppendLine($"            var resultModel = await _{ToCamelCase(serviceClassName)}.CreateAsync(modelToCreate);");
            sb.AppendLine($"            _mock{modelClassName}Repository.Verify(repo => repo.AddAsync(It.IsAny<{modelClassName}>()), Times.Once);");
            sb.AppendLine("            _mockDbContext.Verify(db => db.SaveChangesAsync(It.IsAny<System.Threading.CancellationToken>()), Times.Once);");
            sb.AppendLine("            Assert.NotNull(resultModel);");
            sb.AppendLine($"            Assert.Equal(createdModel.{pkPropertyName}, resultModel.{pkPropertyName});");
            sb.AppendLine("        }");
            sb.AppendLine("    }");
            sb.AppendLine("}");
            return sb.ToString();
        }

        public string GenerateDependencyInjectionExtensions(List<TableDefinitionViewModel> tables, SchemaGeneratorViewModel globalOptions)
        {
            var sb = new StringBuilder();
            string dbContextName = ToPascalCase(globalOptions.DbContextName);

            sb.AppendLine($"// File: {globalOptions.RootNamespace}/Extensions/DependencyInjectionExtensions.cs");
            sb.AppendLine($"namespace {globalOptions.RootNamespace}.Extensions");
            sb.AppendLine("{");
            sb.AppendLine("    using Microsoft.Extensions.DependencyInjection;");
            sb.AppendLine("    using Microsoft.EntityFrameworkCore;");
            sb.AppendLine("    using Microsoft.Extensions.Configuration;");
            sb.AppendLine($"    using {globalOptions.RootNamespace}.Data;");
            sb.AppendLine($"    using {globalOptions.RootNamespace}.Repositories.Interfaces;");
            sb.AppendLine($"    using {globalOptions.RootNamespace}.Repositories.Implementations;");
            if (globalOptions.GenerateServices || globalOptions.GenerateIServices)
            {
                sb.AppendLine($"    using {globalOptions.RootNamespace}.Services.Interfaces;");
                sb.AppendLine($"    using {globalOptions.RootNamespace}.Services.Implementations;");
            }
            sb.AppendLine();
            sb.AppendLine("    public static class DependencyInjectionExtensions");
            sb.AppendLine("    {");
            sb.AppendLine("        public static IServiceCollection AddGeneratedInfrastructure(this IServiceCollection services, IConfiguration configuration)");
            sb.AppendLine("        {");
            sb.AppendLine($"            services.AddDbContext<{dbContextName}>(options =>");
            sb.AppendLine($"                options.UseSqlServer(configuration.GetConnectionString(\"DefaultConnection\")));");
            sb.AppendLine();
            sb.AppendLine("            services.AddScoped(typeof(IRepository<>), typeof(Repository<>));");
            foreach (var table in tables.Where(t => !string.IsNullOrWhiteSpace(t.TableName)))
            {
                string modelClassName = ToPascalCase(table.TableName); // No suffix
                string iRepoName = $"{globalOptions.NamingConventions.RepositoryInterfacePrefix}{modelClassName}{globalOptions.NamingConventions.RepositoryClassSuffix}";
                string repoName = $"{modelClassName}{globalOptions.NamingConventions.RepositoryClassSuffix}";
                sb.AppendLine($"            services.AddScoped<{iRepoName}, {repoName}>();");
            }
            sb.AppendLine();

            if (globalOptions.GenerateServices || globalOptions.GenerateIServices)
            {
                sb.AppendLine("            // Services");
                foreach (var table in tables.Where(t => !string.IsNullOrWhiteSpace(t.TableName)))
                {
                    string modelClassName = ToPascalCase(table.TableName); // No suffix
                    string iServiceName = $"{globalOptions.NamingConventions.ServiceInterfacePrefix}{modelClassName}{globalOptions.NamingConventions.ServiceClassSuffix}";
                    string serviceName = $"{modelClassName}{globalOptions.NamingConventions.ServiceClassSuffix}";
                    sb.AppendLine($"            services.AddScoped<{iServiceName}, {serviceName}>();");
                }
                sb.AppendLine();
            }
            sb.AppendLine("            return services;");
            sb.AppendLine("        }");
            sb.AppendLine("    }");
            sb.AppendLine("}");
            return sb.ToString();
        }

        // Helper methods (GetCSharpDataType, ToPascalCase, ToCamelCase, Pluralize, GetPrimaryKeyDataType, GetPrimaryKeyPropertyName, IsNumericType)
        // remain largely the same, ensure they are robust.
        // AddManualMappers is removed as DTOs are removed.

        public string GetCSharpDataType(string dataType, bool isNullable)
        {
            string result = dataType.ToLowerInvariant() switch
            {
                "int" => "int", "long" => "long", "string" => "string", "bool" => "bool",
                "datetime" => "DateTime", "decimal" => "decimal", "double" => "double",
                "float" => "float", "guid" => "Guid", "byte[]" => "byte[]",
                _ => "object"
            };
            if (isNullable && result != "string" && result != "byte[]" && result != "object") result += "?";
            return result;
        }

        public string ToPascalCase(string name)
        {
            if (string.IsNullOrEmpty(name)) return name;
            return string.Concat(name.Split(new[] { '_', ' ', '-' }, StringSplitOptions.RemoveEmptyEntries)
                                     .Select(s => char.ToUpperInvariant(s[0]) + s.Substring(1).ToLowerInvariant()));
        }
        private string ToCamelCase(string name)
        {
            if (string.IsNullOrEmpty(name)) return name;
            string pascal = ToPascalCase(name);
            return char.ToLowerInvariant(pascal[0]) + pascal.Substring(1);
        }

        public string Pluralize(string name)
        {
            if (string.IsNullOrEmpty(name)) return name;
            if (name.EndsWith("y", StringComparison.OrdinalIgnoreCase) && name.Length > 1 && !"aeiou".Contains(char.ToLowerInvariant(name[name.Length - 2])))
                return name.Substring(0, name.Length - 1) + "ies";
            if (name.EndsWith("s", StringComparison.OrdinalIgnoreCase) || name.EndsWith("x", StringComparison.OrdinalIgnoreCase) ||
                name.EndsWith("z", StringComparison.OrdinalIgnoreCase) || name.EndsWith("ch", StringComparison.OrdinalIgnoreCase) ||
                name.EndsWith("sh", StringComparison.OrdinalIgnoreCase))
                return name + "es";
            return name + "s";
        }

        private string GetPrimaryKeyDataType(TableDefinitionViewModel table)
        {
            var pk = table.Properties.FirstOrDefault(p => p.IsPrimaryKey);
            return pk != null ? GetCSharpDataType(pk.DataType, false) : "object";
        }
        private string GetPrimaryKeyPropertyName(TableDefinitionViewModel table)
        {
            var pk = table.Properties.FirstOrDefault(p => p.IsPrimaryKey);
            return pk != null ? ToPascalCase(pk.PropertyName) : "Id";
        }
        private bool IsNumericType(string dataType) => new[] { "int", "long", "decimal", "double", "float" }.Contains(dataType.ToLowerInvariant());
        // IsCollectionType might not be needed anymore or needs significant rework if kept.
    }
}
