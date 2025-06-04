
// File: MvcDynamicCodeGenerator/Models/PropertyDefinitionViewModel.cs
using System.ComponentModel.DataAnnotations;

namespace MvcDynamicCodeGenerator.Models
{
    public class PropertyDefinitionViewModel
    {
        [Required(ErrorMessage = "Tên thuộc tính là bắt buộc.")]
        [RegularExpression(@"^[a-zA-Z_][a-zA-Z0-9_]*$", ErrorMessage = "Tên thuộc tính chỉ được chứa chữ cái, số và dấu gạch dưới, và không bắt đầu bằng số.")]
        [Display(Name = "Tên Thuộc Tính")]
        public string PropertyName { get; set; }

        [Required(ErrorMessage = "Kiểu dữ liệu là bắt buộc.")]
        [Display(Name = "Kiểu Dữ Liệu")]
        public string DataType { get; set; } = "string"; // Default to string

        [Display(Name = "Là Khóa Chính?")]
        public bool IsPrimaryKey { get; set; }

        [Display(Name = "Cho Phép Null?")]
        public bool IsNullable { get; set; } = true; // Default to true for non-PK fields

        // Foreign Key Related
        [Display(Name = "Là Khóa Ngoại?")]
        public bool IsForeignKey { get; set; }

        [Display(Name = "Bảng Tham Chiếu")]
        public string? ReferencedTableName { get; set; } // Made nullable

        [Display(Name = "Thuộc Tính Tham Chiếu (ở Bảng Tham Chiếu)")]
        public string? ReferencedPropertyName { get; set; } // Made nullable, usually the PK of the referenced table

        [Display(Name = "Tên Thuộc Tính Điều Hướng")]
        public string? NavigationPropertyName { get; set; } // Made nullable

        [Display(Name = "Hành vi khi Xóa (Delete Behavior)")]
        public string? DeleteBehavior { get; set; } = ""; // Made nullable, empty for default. Options: "Cascade", "ClientSetNull", "Restrict", "NoAction"

        [Display(Name = "Tên Ràng Buộc FK (Trong DB)")]
        public string? CustomFKConstraintName { get; set; } // Made nullable


        // Validation Related
        [Display(Name = "Độ dài Max (String)")]
        [Range(1, int.MaxValue, ErrorMessage = "MaxLength phải lớn hơn 0.")]
        public int? MaxLength { get; set; }

        [Display(Name = "Độ dài Min (String)")]
        [Range(0, int.MaxValue, ErrorMessage = "MinLength không được âm.")]
        public int? MinLength { get; set; }

        [Display(Name = "Giá trị Min (Số)")]
        public double? RangeMin { get; set; }

        [Display(Name = "Giá trị Max (Số)")]
        public double? RangeMax { get; set; }

        [Display(Name = "Là Email?")]
        public bool IsEmailAddress { get; set; }

        [Display(Name = "Là Số Điện Thoại?")]
        public bool IsPhoneNumber { get; set; }

        [Display(Name = "Là URL?")]
        public bool IsUrl { get; set; }

        [Display(Name = "Regex Pattern")]
        public string? RegexPattern { get; set; } // Made nullable

        // Database Configuration
        [Display(Name = "Kiểu CSDL (ColumnType)")]
        public string? ColumnTypeName { get; set; } // Made nullable, e.g., "decimal(18,2)", "nvarchar(max)"

        [Display(Name = "Là Timestamp/RowVersion? (byte[])")]
        public bool IsTimestamp { get; set; } // For byte[] type

        [Display(Name = "Là Concurrency Token?")]
        public bool IsConcurrencyToken { get; set; } // General concurrency token
    }
}
