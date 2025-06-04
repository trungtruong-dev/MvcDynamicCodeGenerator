using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace MvcDynamicCodeGenerator.Models
{
    public class TableDefinitionViewModel
    {
        [Required(ErrorMessage = "Tên bảng không được để trống.")]
        [Display(Name = "Tên Bảng")]
        public string TableName { get; set; } = string.Empty;
        public List<PropertyDefinitionViewModel> Properties { get; set; } = new List<PropertyDefinitionViewModel>();
    }
}