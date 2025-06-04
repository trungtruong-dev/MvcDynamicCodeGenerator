using MvcDynamicCodeGenerator.Models;
using System.Text;
using System.Linq;
using System.Collections.Generic;
using System.Globalization;

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
            public string NavigationPropertyName { get; set; }
        }

        public Dictionary<string, List<InverseNavigationInfo>> AnalyzeInverseRelationships(List<TableDefinitionViewModel> tables)
        {
            var inverseRelationships = new Dictionary<string, List<InverseNavigationInfo>>();

            foreach (var table in tables)
            {
                if (string.IsNullOrWhiteSpace(table.TableName)) continue;

                foreach (var prop in table.Properties)
                {
                    if (prop.IsForeignKey && !string.IsNullOrEmpty(prop.ReferencedTableName))
                    {
                        if (!inverseRelationships.ContainsKey(prop.ReferencedTableName))
                        {
                            inverseRelationships[prop.ReferencedTableName] = new List<InverseNavigationInfo>();
                        }
                        inverseRelationships[prop.ReferencedTableName].Add(new InverseNavigationInfo
                        {
                            ReferencingTableName = table.TableName,
                            ForeignKeyPropertyName = prop.PropertyName,
                            NavigationPropertyName = prop.NavigationPropertyName
                        });
                    }
                }
            }
            return inverseRelationships;
        }

        public string GenerateEntityClass(TableDefinitionViewModel table, string rootNamespace,
                                          Dictionary<string, List<InverseNavigationInfo>> allInverseRelationships)
        {
            var sb = new StringBuilder();

            string entityClassName = ToPascalCase(table.TableName);

            sb.AppendLine($"namespace {rootNamespace}.Entities");
            sb.AppendLine("{");
            sb.AppendLine("    using System;");
            sb.AppendLine("    using System.Collections.Generic;");
            sb.AppendLine("    using System.ComponentModel.DataAnnotations;");
            sb.AppendLine("    using System.ComponentModel.DataAnnotations.Schema;");

            sb.AppendLine($"");
            sb.AppendLine($"    public class {entityClassName}");
            sb.AppendLine("    {");

            foreach (var prop in table.Properties)
            {
                string pascalCasePropertyName = ToPascalCase(prop.PropertyName);
                string csharpDataType = GetCSharpDataType(prop.DataType, prop.IsNullable);

                if (prop.IsPrimaryKey) sb.AppendLine($"        [Key]");
                if (prop.MaxLength.HasValue) sb.AppendLine($"        [MaxLength({prop.MaxLength.Value})]");
                if (!prop.IsNullable && !prop.IsPrimaryKey && prop.DataType == "string") sb.AppendLine($"        [Required]");
                if (prop.IsEmailAddress) sb.AppendLine($"        [EmailAddress]");
                if (prop.IsPhoneNumber) sb.AppendLine($"        [Phone]");
                if (prop.IsUrl) sb.AppendLine($"        [Url]");
                if (!string.IsNullOrEmpty(prop.RegexPattern)) sb.AppendLine($"        [RegularExpression(\"{prop.RegexPattern}\")]");
                if (!string.IsNullOrEmpty(prop.ColumnTypeName)) sb.AppendLine($"        [Column(TypeName = \"{prop.ColumnTypeName}\")]");
                if (prop.IsTimestamp) sb.AppendLine($"        [Timestamp]");
                if (prop.IsConcurrencyToken) sb.AppendLine($"        [ConcurrencyCheck]");

                sb.AppendLine($"        public {csharpDataType} {pascalCasePropertyName} {{ get; set; }}");

                if (prop.IsForeignKey && !string.IsNullOrEmpty(prop.NavigationPropertyName) && !string.IsNullOrEmpty(prop.ReferencedTableName))
                {
                    string pascalCaseNavPropertyName = ToPascalCase(prop.NavigationPropertyName);
                    string referencedEntityNamePascalCase = ToPascalCase(prop.ReferencedTableName);
                    string navPropertyNullableSuffix = prop.IsNullable ? "?" : "";

                    if (pascalCasePropertyName != $"{referencedEntityNamePascalCase}Id")
                    {
                        sb.AppendLine($"        [ForeignKey(\"{pascalCasePropertyName}\")]");
                    }
                    
                    sb.AppendLine($"        public virtual {referencedEntityNamePascalCase}{navPropertyNullableSuffix} {pascalCaseNavPropertyName} {{ get; set; }}");
                }
            }

            if (allInverseRelationships.TryGetValue(table.TableName, out var referencingTables))
            {
                foreach (var inverseNav in referencingTables)
                {
                    string collectionPropertyName = Pluralize(ToPascalCase(inverseNav.ReferencingTableName));
                    if (!table.Properties.Any(p => ToPascalCase(p.PropertyName) == collectionPropertyName) &&
                        !table.Properties.Any(p => ToPascalCase(p.NavigationPropertyName) == collectionPropertyName))
                    {
                         sb.AppendLine($"");
                         sb.AppendLine($"        public virtual ICollection<{ToPascalCase(inverseNav.ReferencingTableName)}> {collectionPropertyName} {{ get; set; }} = new List<{ToPascalCase(inverseNav.ReferencingTableName)}>();");
                    }
                }
            }

            sb.AppendLine("    }");
            sb.AppendLine("}");
            return sb.ToString();
        }

        public string GenerateDbContextClass(List<TableDefinitionViewModel> tables, string rootNamespace)
        {
            var sb = new StringBuilder();

            sb.AppendLine($"namespace {rootNamespace}.Data");
            sb.AppendLine("{");
            sb.AppendLine("    using Microsoft.EntityFrameworkCore;");
            sb.AppendLine($"    using {rootNamespace}.Entities;");
            sb.AppendLine("    using System.Linq;");
            sb.AppendLine();
            sb.AppendLine($"    public class ApplicationDbContext : DbContext");
            sb.AppendLine("    {");
            sb.AppendLine($"        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options)");
            sb.AppendLine("        {");
            sb.AppendLine("        }");
            sb.AppendLine("");

            foreach (var table in tables)
            {
                if (string.IsNullOrWhiteSpace(table.TableName)) continue;
                string entityName = ToPascalCase(table.TableName);
                string dbSetName = Pluralize(entityName);
                sb.AppendLine($"        public DbSet<{entityName}> {dbSetName} {{ get; set; }}");
            }

            sb.AppendLine("");
            sb.AppendLine("        protected override void OnModelCreating(ModelBuilder modelBuilder)");
            sb.AppendLine("        {");
            sb.AppendLine("            base.OnModelCreating(modelBuilder);");
            sb.AppendLine("");

            var allInverseRelationships = AnalyzeInverseRelationships(tables);

            foreach (var table in tables)
            {
                if (string.IsNullOrWhiteSpace(table.TableName)) continue;
                string entityNamePascalCase = ToPascalCase(table.TableName);

                // Primary Key Configuration
                var primaryKeys = table.Properties.Where(p => p.IsPrimaryKey).ToList();
                if (primaryKeys.Any())
                {
                    if (primaryKeys.Count == 1)
                    {
                        string pkPropertyName = ToPascalCase(primaryKeys.First().PropertyName);
                        sb.AppendLine($"            modelBuilder.Entity<{entityNamePascalCase}>().HasKey(e => e.{pkPropertyName});");
                    }
                    else // Composite Primary Key
                    {
                        sb.AppendLine($"            modelBuilder.Entity<{entityNamePascalCase}>().HasKey(e => new {{ {string.Join(", ", primaryKeys.Select(p => $"e.{ToPascalCase(p.PropertyName)}"))} }});");
                    }
                }

                // Foreign Key Configuration
                foreach (var prop in table.Properties.Where(p => p.IsForeignKey))
                {
                    if (string.IsNullOrEmpty(prop.NavigationPropertyName) || string.IsNullOrEmpty(prop.ReferencedTableName)) continue;

                    string navPropertyName = ToPascalCase(prop.NavigationPropertyName);
                    string fkScalarPropertyName = ToPascalCase(prop.PropertyName);
                    string referencedEntityPascalCase = ToPascalCase(prop.ReferencedTableName);

                    sb.Append($"            modelBuilder.Entity<{entityNamePascalCase}>()\n" +
                              $"                .HasOne(e => e.{navPropertyName})");

                    if (allInverseRelationships.TryGetValue(referencedEntityPascalCase, out var principalInverseNavs))
                    {
                        var matchingInverseNav = principalInverseNavs.FirstOrDefault(
                            inv => ToPascalCase(inv.ReferencingTableName) == entityNamePascalCase &&
                                   ToPascalCase(inv.ForeignKeyPropertyName) == fkScalarPropertyName);

                        if (matchingInverseNav != null)
                        {
                            string inverseCollectionName = Pluralize(ToPascalCase(matchingInverseNav.ReferencingTableName));
                            sb.Append($"\n                .WithMany(p => p.{inverseCollectionName})");
                        }
                        else
                        {
                            sb.Append("\n                .WithMany()");
                        }
                    }
                    else
                    {
                        sb.Append("\n                .WithMany()");
                    }

                    sb.Append($"\n                .HasForeignKey(e => e.{fkScalarPropertyName});");
                    sb.AppendLine("");
                }
            }

            sb.AppendLine("        }");
            sb.AppendLine("    }");
            sb.AppendLine("}");
            return sb.ToString();
        }

        // --- Generic IRepository (as a base if you want to keep it) ---
        public string GenerateGenericIRepository(string rootNamespace)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"namespace {rootNamespace}.Repositories.Interfaces");
            sb.AppendLine("{");
            sb.AppendLine("    using System.Linq.Expressions;");
            sb.AppendLine("    using System.Collections.Generic;");
            sb.AppendLine("    using System.Threading.Tasks;");
            sb.AppendLine();
            sb.AppendLine("    public interface IRepository<TEntity>");
            sb.AppendLine("        where TEntity : class");
            sb.AppendLine("    {");
            sb.AppendLine("        Task<TEntity?> GetByIdAsync(object id);");
            sb.AppendLine("        Task<IEnumerable<TEntity>> GetAllAsync();");
            sb.AppendLine("        Task<IEnumerable<TEntity>> FindAsync(Expression<System.Func<TEntity, bool>> predicate);");
            sb.AppendLine("        Task AddAsync(TEntity entity);");
            sb.AppendLine("        Task AddRangeAsync(IEnumerable<TEntity> entities);");
            sb.AppendLine("        void Update(TEntity entity);");
            sb.AppendLine("        void Remove(TEntity entity);"); // Fixed: `void Remove` instead of `void void Remove`
            sb.AppendLine("        void RemoveRange(IEnumerable<TEntity> entities);");
            sb.AppendLine("        Task<int> CountAsync(Expression<System.Func<TEntity, bool>>? predicate = null);");
            sb.AppendLine("    }");
            sb.AppendLine("}");
            return sb.ToString();
        }

        // --- Generic Repository Implementation (as a base if you want to keep it) ---
        public string GenerateGenericRepositoryClass(string rootNamespace, string dbContextName)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"namespace {rootNamespace}.Repositories.Implementations");
            sb.AppendLine("{");
            sb.AppendLine("    using Microsoft.EntityFrameworkCore;");
            sb.AppendLine("    using System.Linq.Expressions;");
            sb.AppendLine("    using System.Collections.Generic;");
            sb.AppendLine("    using System.Linq;");
            sb.AppendLine("    using System.Threading.Tasks;");
            sb.AppendLine($"    using {rootNamespace}.Data;");
            sb.AppendLine($"    using {rootNamespace}.Repositories.Interfaces;"); // Import the generic interface
            sb.AppendLine();
            sb.AppendLine("    public class Repository<TEntity> : IRepository<TEntity>");
            sb.AppendLine("        where TEntity : class");
            sb.AppendLine("    {");
            sb.AppendLine($"        protected readonly {dbContextName} _context;");
            sb.AppendLine();
            sb.AppendLine($"        public Repository({dbContextName} context)");
            sb.AppendLine("        {");
            sb.AppendLine("            _context = context;");
            sb.AppendLine("        }");
            sb.AppendLine();
            sb.AppendLine("        public async Task<TEntity?> GetByIdAsync(object id)");
            sb.AppendLine("        {");
            sb.AppendLine("            return await _context.Set<TEntity>().FindAsync(id);");
            sb.AppendLine("        }");
            sb.AppendLine();
            sb.AppendLine("        public async Task<IEnumerable<TEntity>> GetAllAsync()");
            sb.AppendLine("        {");
            sb.AppendLine("            return await _context.Set<TEntity>().ToListAsync();");
            sb.AppendLine("        }");
            sb.AppendLine();
            sb.AppendLine("        public async Task<IEnumerable<TEntity>> FindAsync(Expression<System.Func<TEntity, bool>> predicate)");
            sb.AppendLine("        {");
            sb.AppendLine("            return await _context.Set<TEntity>().Where(predicate).ToListAsync();");
            sb.AppendLine("        }");
            sb.AppendLine();
            sb.AppendLine("        public async Task AddAsync(TEntity entity)");
            sb.AppendLine("        {");
            sb.AppendLine("            await _context.Set<TEntity>().AddAsync(entity);");
            sb.AppendLine("        }");
            sb.AppendLine();
            sb.AppendLine("        public async Task AddRangeAsync(IEnumerable<TEntity> entities)");
            sb.AppendLine("        {");
            sb.AppendLine("            await _context.Set<TEntity>().AddRangeAsync(entities);");
            sb.AppendLine("        }");
            sb.AppendLine();
            sb.AppendLine("        public void Update(TEntity entity)");
            sb.AppendLine("        {");
            sb.AppendLine("            _context.Set<TEntity>().Update(entity);");
            sb.AppendLine("        }");
            sb.AppendLine();
            sb.AppendLine("        public void Remove(TEntity entity)");
            sb.AppendLine("        {");
            sb.AppendLine("            _context.Set<TEntity>().Remove(entity);");
            sb.AppendLine("        }");
            sb.AppendLine();
            sb.AppendLine("        public void RemoveRange(IEnumerable<TEntity> entities)");
            sb.AppendLine("        {");
            sb.AppendLine("            _context.Set<TEntity>().RemoveRange(entities);");
            sb.AppendLine("        }");
            sb.AppendLine();
            sb.AppendLine("        public async Task<int> CountAsync(Expression<System.Func<TEntity, bool>>? predicate = null)");
            sb.AppendLine("        {");
            sb.AppendLine("            if (predicate == null)");
            sb.AppendLine("            {");
            sb.AppendLine("                return await _context.Set<TEntity>().CountAsync();");
            sb.AppendLine("            }");
            sb.AppendLine("            return await _context.Set<TEntity>().CountAsync(predicate);");
            sb.AppendLine("        }");
            sb.AppendLine("    }");
            sb.AppendLine("}");
            return sb.ToString();
        }

        // --- NEW: Generate Specific I[Entity]Repository Interface ---
        // This interface will inherit from the generic IRepository<TEntity>
        public string GenerateSpecificIRepository(TableDefinitionViewModel table, string rootNamespace)
        {
            var sb = new StringBuilder();
            string entityClassName = ToPascalCase(table.TableName);
            string iRepositoryName = $"I{entityClassName}Repository";

            sb.AppendLine($"namespace {rootNamespace}.Repositories.Interfaces");
            sb.AppendLine("{");
            sb.AppendLine($"    using {rootNamespace}.Entities;"); // Reference to the entity namespace
            sb.AppendLine("    using System.Collections.Generic;"); // For IEnumerable
            sb.AppendLine("    using System.Threading.Tasks;"); // For async methods
            sb.AppendLine();
            // Inherit from the generic IRepository
            sb.AppendLine($"    public interface {iRepositoryName} : IRepository<{entityClassName}>");
            sb.AppendLine("    {");
            // Add specific methods here if needed, e.g.,
            // sb.AppendLine($"        Task<IEnumerable<{entityClassName}>> Get{Pluralize(entityClassName)}ByCustomCriteriaAsync(string someCriteria);");
            sb.AppendLine("    }");
            sb.AppendLine("}");
            return sb.ToString();
        }

        // --- NEW: Generate Specific [Entity]Repository Class ---
        // This class will inherit from the generic Repository<TEntity>
        public string GenerateSpecificRepositoryClass(TableDefinitionViewModel table, string rootNamespace, string dbContextName)
        {
            var sb = new StringBuilder();
            string entityClassName = ToPascalCase(table.TableName);
            string repositoryName = $"{entityClassName}Repository";
            string iRepositoryName = $"I{entityClassName}Repository"; // Name of the specific interface

            sb.AppendLine($"namespace {rootNamespace}.Repositories.Implementations");
            sb.AppendLine("{");
            sb.AppendLine("    using Microsoft.EntityFrameworkCore;");
            sb.AppendLine("    using System.Linq;"); // For Linq extensions like .Where, .ToListAsync
            sb.AppendLine("    using System.Collections.Generic;");
            sb.AppendLine("    using System.Threading.Tasks;");
            sb.AppendLine($"    using {rootNamespace}.Data;"); // Import DbContext namespace
            sb.AppendLine($"    using {rootNamespace}.Entities;"); // Import Entity namespace
            sb.AppendLine($"    using {rootNamespace}.Repositories.Interfaces;"); // Import the specific interface
            sb.AppendLine();
            // Inherit from the generic Repository and implement the specific interface
            sb.AppendLine($"    public class {repositoryName} : Repository<{entityClassName}>, {iRepositoryName}");
            sb.AppendLine("    {");
            sb.AppendLine($"        public {repositoryName}({dbContextName} context) : base(context)");
            sb.AppendLine("        {");
            sb.AppendLine("        }");
            // Add specific methods implementation here if needed
            // sb.AppendLine();
            // sb.AppendLine($"        public async Task<IEnumerable<{entityClassName}>> Get{Pluralize(entityClassName)}ByCustomCriteriaAsync(string someCriteria)");
            // sb.AppendLine($"        {{");
            // sb.AppendLine($"            return await _context.Set<{entityClassName}>().Where(e => e.SomeProperty.Contains(someCriteria)).ToListAsync();");
            // sb.AppendLine($"        }}");
            sb.AppendLine("    }");
            sb.AppendLine("}");
            return sb.ToString();
        }

        // --- Helper methods ---
        public string GetCSharpDataType(string dataType, bool isNullable)
        {
            string result = dataType.ToLower() switch
            {
                "int" => "int",
                "long" => "long",
                "string" => "string",
                "bool" => "bool",
                "datetime" => "DateTime",
                "decimal" => "decimal",
                "double" => "double",
                "float" => "float",
                "guid" => "Guid",
                "byte[]" => "byte[]",
                _ => "object"
            };

            if (isNullable && result != "string" && result != "byte[]")
            {
                result += "?";
            }
            return result;
        }

        public string ToPascalCase(string name)
        {
            if (string.IsNullOrEmpty(name)) return name;
            return _textInfo.ToTitleCase(name.Replace("_", " ")).Replace(" ", "");
        }

        public string Pluralize(string name)
        {
            if (string.IsNullOrEmpty(name)) return name;

            // Using simple pluralization rules
            if (name.EndsWith("s", StringComparison.OrdinalIgnoreCase) ||
                name.EndsWith("x", StringComparison.OrdinalIgnoreCase) ||
                name.EndsWith("z", StringComparison.OrdinalIgnoreCase) ||
                name.EndsWith("ch", StringComparison.OrdinalIgnoreCase) ||
                name.EndsWith("sh", StringComparison.OrdinalIgnoreCase))
            {
                return name + "es";
            }
            if (name.EndsWith("y", StringComparison.OrdinalIgnoreCase) && name.Length > 1 && !"aeiou".Contains(char.ToLowerInvariant(name[name.Length - 2])))
            {
                return name.Substring(0, name.Length - 1) + "ies";
            }
            
            return name + "s";
        }
    }
}