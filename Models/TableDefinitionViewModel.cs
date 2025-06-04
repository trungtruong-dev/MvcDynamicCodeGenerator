
// File: MvcDynamicCodeGenerator/Models/TableDefinitionViewModel.cs
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace MvcDynamicCodeGenerator.Models
{
    public class TableDefinitionViewModel
    {
        [Required(ErrorMessage = "Tên bảng là bắt buộc.")]
        [RegularExpression(@"^[a-zA-Z_][a-zA-Z0-9_]*$", ErrorMessage = "Tên bảng chỉ được chứa chữ cái, số và dấu gạch dưới, và không bắt đầu bằng số.")]
        [Display(Name = "Tên Bảng")]
        public string TableName { get; set; }

        public List<PropertyDefinitionViewModel> Properties { get; set; } = new List<PropertyDefinitionViewModel>();

        [Display(Name = "Bật Soft Delete cho bảng này")]
        public bool? EnableSoftDelete { get; set; } // Nullable to allow inheriting from global or overriding

        public TableDefinitionViewModel()
        {
            // Ensure at least one property (e.g., Id) when a table is initialized programmatically
            // if (!Properties.Any() && string.IsNullOrEmpty(TableName)) // Only if it's truly a new, unconfigured table
            // {
            //     Properties.Add(new PropertyDefinitionViewModel { PropertyName = "Id", DataType = "int", IsPrimaryKey = true, IsNullable = false });
            // }
        }
    }
}
