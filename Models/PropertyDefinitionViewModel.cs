using System.ComponentModel.DataAnnotations;

namespace MvcDynamicCodeGenerator.Models
{
    public class PropertyDefinitionViewModel
    {
        [Required(ErrorMessage = "Tên thuộc tính không được để trống.")]
        [Display(Name = "Tên Thuộc Tính")]
        public string PropertyName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Kiểu dữ liệu không được để trống.")]
        [Display(Name = "Kiểu dữ liệu")]
        public string DataType { get; set; } = "string";

        [Display(Name = "Khóa chính?")]
        public bool IsPrimaryKey { get; set; } = false;

        [Display(Name = "Cho phép Null?")]
        public bool IsNullable { get; set; } = true;

        // Validation Options
        [Display(Name = "Max Len (string)")]
        [Range(1, int.MaxValue, ErrorMessage = "Độ dài tối đa phải lớn hơn 0.")]
        public int? MaxLength { get; set; }

        [Display(Name = "Min Len (string)")]
        [Range(0, int.MaxValue, ErrorMessage = "Độ dài tối thiểu không được âm.")]
        public int? MinLength { get; set; }

        [Display(Name = "Range Min (numeric)")]
        public double? RangeMin { get; set; }

        [Display(Name = "Range Max (numeric)")]
        public double? RangeMax { get; set; }

        [Display(Name = "Là Email?")]
        public bool IsEmailAddress { get; set; } = false;

        [Display(Name = "Là SĐT?")]
        public bool IsPhoneNumber { get; set; } = false;

        [Display(Name = "Là URL?")]
        public bool IsUrl { get; set; } = false;

        [Display(Name = "Regex Pattern")]
        public string? RegexPattern { get; set; }

        // Foreign Key Options
        [Display(Name = "Là Khóa Ngoại?")]
        public bool IsForeignKey { get; set; } = false;

        [Display(Name = "Bảng Tham Chiếu")]
        public string? ReferencedTableName { get; set; }

        [Display(Name = "Thuộc tính Khóa Chính Tham Chiếu")]
        public string? ReferencedPropertyName { get; set; } // Thường là "Id"

        [Display(Name = "Tên Thuộc Tính Điều Hướng")]
        public string? NavigationPropertyName { get; set; }

        // Database Configuration Options
        [Display(Name = "Kiểu CSDL (ColumnType)")]
        public string? ColumnTypeName { get; set; }

        [Display(Name = "Là RowVersion (Timestamp)?")]
        public bool IsTimestamp { get; set; } = false;

        [Display(Name = "Là Concurrency Token?")]
        public bool IsConcurrencyToken { get; set; } = false;
    }
}