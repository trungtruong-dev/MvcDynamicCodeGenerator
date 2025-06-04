using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace MvcDynamicCodeGenerator.Models
{
    public class SchemaGeneratorViewModel
    {
        [Required]
        [Display(Name = "Namespace Gốc")]
        public string RootNamespace { get; set; } = "MyProject.Generated";

        [Required]
        [Display(Name = "Tên DbContext")]
        public string DbContextName { get; set; } = "ApplicationDbContext";

        [Display(Name = "Tạo IServices")]
        public bool GenerateIServices { get; set; } = true;

        [Display(Name = "Tạo Services")]
        public bool GenerateServices { get; set; } = true;

        [Display(Name = "Chỉ Service Bất Đồng Bộ")]
        public bool AsyncServiceOnly { get; set; } = true;

        [Display(Name = "Tạo Unit Tests")]
        public bool GenerateUnitTests { get; set; } = false;

        public List<TableDefinitionViewModel> Tables { get; set; } = new List<TableDefinitionViewModel>();
    }
}