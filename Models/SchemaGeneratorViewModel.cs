// File: MvcDynamicCodeGenerator/Models/SchemaGeneratorViewModel.cs
using Microsoft.AspNetCore.Mvc.Rendering;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq; // Added for .Any()

namespace MvcDynamicCodeGenerator.Models
{
    public enum CodeGenerationStyle
    {
        [Display(Name = "Data Annotations & Fluent API (Mặc định)")]
        AnnotationsAndFluentApi,
        [Display(Name = "Chỉ Data Annotations")]
        AnnotationsOnly,
        [Display(Name = "Chỉ Fluent API")]
        FluentApiOnly
    }

    // NamingConventionCase enum can be kept if you plan other naming convention options in the future
    public enum NamingConventionCase
    {
        PascalCase,
        CamelCase,
        SnakeCase
    }

    public class NamingConventionOptionsViewModel
    {
        // EntityNameSuffix is removed as per request.
        // public string EntityNameSuffix { get; set; } = ""; 

        [Display(Name = "Tiền tố Interface Repository")]
        public string RepositoryInterfacePrefix { get; set; } = "I";

        [Display(Name = "Hậu tố Class Repository")]
        public string RepositoryClassSuffix { get; set; } = "Repository";

        [Display(Name = "Tiền tố Interface Service")]
        public string ServiceInterfacePrefix { get; set; } = "I";

        [Display(Name = "Hậu tố Class Service")]
        public string ServiceClassSuffix { get; set; } = "Service";
    }

    public class SchemaGeneratorViewModel
    {
        [Required(ErrorMessage = "RootNamespace là bắt buộc.")]
        [Display(Name = "Namespace Gốc")]
        public string RootNamespace { get; set; } = "MyProject.Generated";

        [Required(ErrorMessage = "Tên DbContext là bắt buộc.")]
        [Display(Name = "Tên DbContext")]
        public string DbContextName { get; set; } = "ApplicationDbContext";

        public List<TableDefinitionViewModel> Tables { get; set; } = new List<TableDefinitionViewModel>();

        [Display(Name = "Sinh Interface Service (ví dụ: IProductService)")]
        public bool GenerateIServices { get; set; } = true;

        [Display(Name = "Sinh Class Service (ví dụ: ProductService)")]
        public bool GenerateServices { get; set; } = true;

        [Display(Name = "Service chỉ có phương thức Async")]
        public bool AsyncServiceOnly { get; set; } = true;

        [Display(Name = "Sinh Unit Tests (Cơ bản)")]
        public bool GenerateUnitTests { get; set; } = false;

        [Display(Name = "Kiểu cấu hình Code")]
        public CodeGenerationStyle ConfigurationStyle { get; set; } = CodeGenerationStyle.AnnotationsAndFluentApi;

        [Display(Name = "Bật Soft Delete toàn cục (thêm cột IsDeleted)")]
        public bool EnableSoftDeleteGlobally { get; set; } = false;

        [Display(Name = "Sinh file Extensions cho Dependency Injection")]
        public bool GenerateDependencyInjectionExtensions { get; set; } = true;

        public NamingConventionOptionsViewModel NamingConventions { get; set; } = new NamingConventionOptionsViewModel();

        public SchemaGeneratorViewModel()
        {
            if (!Tables.Any())
            {
                Tables.Add(new TableDefinitionViewModel
                {
                    TableName = "Products", // Ví dụ tên bảng
                    Properties = new List<PropertyDefinitionViewModel>
                    {
                        new PropertyDefinitionViewModel { PropertyName = "Id", DataType = "int", IsPrimaryKey = true, IsNullable = false },
                        new PropertyDefinitionViewModel { PropertyName = "Name", DataType = "string", IsNullable = false, MaxLength = 200 }
                    }
                });
            }
        }
    }
}
