using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using MvcDynamicCodeGenerator.Models;
using MvcDynamicCodeGenerator.Services;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System;
using System.IO;
using System.IO.Compression;

namespace MvcDynamicCodeGenerator.Controllers
{
    public class GeneratorController : Controller
    {
        private readonly CodeGeneratorService _codeGeneratorService;
        private readonly ILogger<GeneratorController> _logger;

        public GeneratorController(CodeGeneratorService codeGeneratorService, ILogger<GeneratorController> logger)
        {
            _codeGeneratorService = codeGeneratorService;
            _logger = logger;
        }

        [HttpGet]
        public IActionResult Index()
        {
            var model = new SchemaGeneratorViewModel
            {
                RootNamespace = "MyProject.Generated",
                DbContextName = "ApplicationDbContext",
                GenerateIServices = true,
                GenerateServices = true,
                AsyncServiceOnly = true,
                GenerateUnitTests = false
            };

            // Dữ liệu mẫu
            var productsTable = new TableDefinitionViewModel
            {
                TableName = "Products",
                Properties = new List<PropertyDefinitionViewModel>
                {
                    new PropertyDefinitionViewModel { PropertyName = "Id", DataType = "int", IsPrimaryKey = true, IsNullable = false },
                    new PropertyDefinitionViewModel { PropertyName = "Name", DataType = "string", IsNullable = false, MaxLength = 255 }
                }
            };
            model.Tables.Add(productsTable);

            var categoriesTable = new TableDefinitionViewModel
            {
                TableName = "Categories",
                Properties = new List<PropertyDefinitionViewModel>
                {
                    new PropertyDefinitionViewModel { PropertyName = "CategoryId", DataType = "int", IsPrimaryKey = true, IsNullable = false },
                    new PropertyDefinitionViewModel { PropertyName = "CategoryName", DataType = "string", IsNullable = false, MaxLength = 100 }
                }
            };
            model.Tables.Add(categoriesTable);

            var ordersTable = new TableDefinitionViewModel
            {
                TableName = "Orders",
                Properties = new List<PropertyDefinitionViewModel>
                {
                    new PropertyDefinitionViewModel { PropertyName = "OrderId", DataType = "int", IsPrimaryKey = true, IsNullable = false },
                    new PropertyDefinitionViewModel
                    {
                        PropertyName = "ProductId",
                        DataType = "int",
                        IsNullable = false,
                        IsForeignKey = true,
                        ReferencedTableName = "Products",
                        ReferencedPropertyName = "Id",
                        NavigationPropertyName = "Product"
                    },
                    new PropertyDefinitionViewModel { PropertyName = "OrderDate", DataType = "DateTime", IsNullable = true },
                    new PropertyDefinitionViewModel { PropertyName = "Quantity", DataType = "int", IsNullable = false }
                }
            };
            model.Tables.Add(ordersTable);

            var productDetailsTable = new TableDefinitionViewModel
            {
                TableName = "ProductDetails",
                Properties = new List<PropertyDefinitionViewModel>
                {
                    new PropertyDefinitionViewModel { PropertyName = "DetailId", DataType = "int", IsPrimaryKey = true, IsNullable = false },
                    new PropertyDefinitionViewModel
                    {
                        PropertyName = "ProductId",
                        DataType = "int",
                        IsNullable = false,
                        IsForeignKey = true,
                        ReferencedTableName = "Products",
                        ReferencedPropertyName = "Id",
                        NavigationPropertyName = "Product"
                    },
                    new PropertyDefinitionViewModel { PropertyName = "Description", DataType = "string", IsNullable = true, MaxLength = 500 }
                }
            };
            model.Tables.Add(productDetailsTable);

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> GenerateSchema([FromForm] SchemaGeneratorViewModel model)
        {
            if (!ModelState.IsValid)
            {
                return View("Index", model);
            }

            string jobId = Guid.NewGuid().ToString();
            JobStatusManager.SetJobStatus(jobId, JobStatusManager.StatusQueued, "Đang chờ xử lý...", null);

            var validTables = model.Tables.Where(t => !string.IsNullOrWhiteSpace(t.TableName)).ToList();

            _ = Task.Run(async () =>
            {
                try
                {
                    JobStatusManager.SetJobStatus(jobId, JobStatusManager.StatusProcessing, "Đang tạo mã...", null);
                    _logger.LogInformation("[Job {JobId}] Bắt đầu tạo mã cho Namespace: {RootNamespace}", jobId, model.RootNamespace);
                    _logger.LogInformation("[Job {JobId}] Tên DbContext: {DbContextName}", jobId, model.DbContextName);
                    _logger.LogInformation("[Job {JobId}] Tổng số bảng hợp lệ: {TableCount}", jobId, validTables.Count);

                    string tempBaseDir = Path.Combine(Path.GetTempPath(), $"GeneratedCode_{jobId}");
                    Directory.CreateDirectory(tempBaseDir);

                    // Create subdirectories
                    string entitiesDir = Path.Combine(tempBaseDir, "Entities");
                    string dataDir = Path.Combine(tempBaseDir, "Data");
                    string repositoriesDir = Path.Combine(tempBaseDir, "Repositories");
                    string repositoryInterfacesDir = Path.Combine(repositoriesDir, "Interfaces"); // NEW: Interfaces sub-directory
                    string repositoryImplementationsDir = Path.Combine(repositoriesDir, "Implementations"); // NEW: Implementations sub-directory
                    
                    Directory.CreateDirectory(entitiesDir);
                    Directory.CreateDirectory(dataDir);
                    Directory.CreateDirectory(repositoriesDir);
                    Directory.CreateDirectory(repositoryInterfacesDir); // Create Interface directory
                    Directory.CreateDirectory(repositoryImplementationsDir); // Create Implementations directory


                    // Giai đoạn 1: Phân tích mối quan hệ
                    var allInverseRelationships = _codeGeneratorService.AnalyzeInverseRelationships(validTables);

                    // Giai đoạn 2: Sinh mã cho từng Entity
                    foreach (var table in validTables)
                    {
                        string entityCode = _codeGeneratorService.GenerateEntityClass(table, model.RootNamespace, allInverseRelationships);
                        string entityFilePath = Path.Combine(entitiesDir, $"{_codeGeneratorService.ToPascalCase(table.TableName)}.cs");
                        await System.IO.File.WriteAllTextAsync(entityFilePath, entityCode);
                        _logger.LogInformation("[Job {JobId}] Đã sinh Entity: {TableName}.cs", jobId, table.TableName);
                    }

                    // Sinh mã cho DbContext
                    string dbContextCode = _codeGeneratorService.GenerateDbContextClass(validTables, model.RootNamespace);
                    string dbContextFilePath = Path.Combine(dataDir, $"{model.DbContextName}.cs");
                    await System.IO.File.WriteAllTextAsync(dbContextFilePath, dbContextCode);
                    _logger.LogInformation("[Job {JobId}] Đã sinh DbContext: {DbContextName}.cs", jobId, model.DbContextName);

                    // NEW: Sinh mã cho Generic IRepository (base interface)
                    string genericIRepositoryCode = _codeGeneratorService.GenerateGenericIRepository(model.RootNamespace);
                    string genericIRepositoryFilePath = Path.Combine(repositoryInterfacesDir, "IRepository.cs");
                    await System.IO.File.WriteAllTextAsync(genericIRepositoryFilePath, genericIRepositoryCode);
                    _logger.LogInformation("[Job {JobId}] Đã sinh Generic IRepository.cs", jobId);

                    // NEW: Sinh mã cho Generic Repository (base implementation)
                    string genericRepositoryCode = _codeGeneratorService.GenerateGenericRepositoryClass(model.RootNamespace, model.DbContextName);
                    string genericRepositoryFilePath = Path.Combine(repositoryImplementationsDir, "Repository.cs");
                    await System.IO.File.WriteAllTextAsync(genericRepositoryFilePath, genericRepositoryCode);
                    _logger.LogInformation("[Job {JobId}] Đã sinh Generic Repository.cs", jobId);

                    // NEW: Sinh mã cho Specific I[Entity]Repository và [Entity]Repository cho mỗi bảng
                    foreach (var table in validTables)
                    {
                        string entityClassName = _codeGeneratorService.ToPascalCase(table.TableName);

                        string specificIRepositoryCode = _codeGeneratorService.GenerateSpecificIRepository(table, model.RootNamespace);
                        string specificIRepositoryFilePath = Path.Combine(repositoryInterfacesDir, $"I{entityClassName}Repository.cs");
                        await System.IO.File.WriteAllTextAsync(specificIRepositoryFilePath, specificIRepositoryCode);
                        _logger.LogInformation("[Job {JobId}] Đã sinh I{EntityName}Repository.cs", jobId, entityClassName);

                        string specificRepositoryCode = _codeGeneratorService.GenerateSpecificRepositoryClass(table, model.RootNamespace, model.DbContextName);
                        string specificRepositoryFilePath = Path.Combine(repositoryImplementationsDir, $"{entityClassName}Repository.cs");
                        await System.IO.File.WriteAllTextAsync(specificRepositoryFilePath, specificRepositoryCode);
                        _logger.LogInformation("[Job {JobId}] Đã sinh {EntityName}Repository.cs", jobId, entityClassName);
                    }
                    
                    // ... (Logic sinh IServices, Services, Unit Tests sẽ được thêm vào đây sau) ...

                    await Task.Delay(2000); // Simulate some processing time

                    string zipFileName = $"GeneratedCode_{jobId}.zip";
                    string zipFilePath = Path.Combine(Path.GetTempPath(), zipFileName);
                    ZipFile.CreateFromDirectory(tempBaseDir, zipFilePath);

                    JobStatusManager.SetJobStatus(jobId, JobStatusManager.StatusCompleted, "Mã đã được tạo thành công!", zipFileName);
                    _logger.LogInformation("[Job {JobId}] Hoàn tất tạo mã và tạo file ZIP: {ZipFileName}", jobId, zipFileName);

                    Directory.Delete(tempBaseDir, true);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[Job {JobId}] Đã xảy ra lỗi trong quá trình tạo mã.", jobId);
                    JobStatusManager.SetJobStatus(jobId, JobStatusManager.StatusError, $"Đã xảy ra lỗi: {ex.Message}", null);
                }
            });
            
            await Task.Yield();

            return Json(new { success = true, jobId = jobId, message = "Quá trình tạo mã đã được khởi tạo." });
        }

        [HttpGet]
        public IActionResult GetJobStatus(string jobId)
        {
            var statusData = JobStatusManager.GetJobStatus(jobId);
            if (statusData == null)
            {
                return NotFound(new { status = JobStatusManager.StatusNotFound, message = "Không tìm thấy công việc." });
            }

            return Json(new
            {
                status = statusData.Value.Status,
                message = statusData.Value.Message,
                downloadFileName = statusData.Value.DownloadFileName
            });
        }

        [HttpGet]
        public IActionResult DownloadGeneratedCode(string fileName)
        {
            string filePath = Path.Combine(Path.GetTempPath(), fileName);
            if (!System.IO.File.Exists(filePath))
            {
                return NotFound($"File '{fileName}' không tìm thấy hoặc đã bị xóa.");
            }

            var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);

            return File(stream, "application/zip", fileName);
        }
    }

    public static class JobStatusManager
    {
        public const string StatusQueued = "Queued";
        public const string StatusProcessing = "Processing";
        public const string StatusCompleted = "Completed";
        public const string StatusError = "Error";
        public const string StatusNotFound = "NotFound";

        private static readonly Dictionary<string, (string Status, string Message, string? DownloadFileName)> _jobStatuses = new Dictionary<string, (string, string, string?)>();
        private static readonly object _lock = new object();

        public static void SetJobStatus(string jobId, string status, string message, string? downloadFileName)
        {
            lock (_lock)
            {
                _jobStatuses[jobId] = (status, message, downloadFileName);
            }
        }

        public static (string Status, string Message, string? DownloadFileName)? GetJobStatus(string jobId)
        {
            lock (_lock)
            {
                if (_jobStatuses.TryGetValue(jobId, out var status))
                {
                    return status;
                }
                return null;
            }
        }
    }
}