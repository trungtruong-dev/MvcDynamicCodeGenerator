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
            var model = new SchemaGeneratorViewModel();
            model.RootNamespace = "MyCompany.MyApp";
            model.DbContextName = "AppDbContext";
            // EntityNameSuffix is removed from NamingConventionOptionsViewModel, so no need to set it here.
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> GenerateSchema([FromForm] SchemaGeneratorViewModel model)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    var errors = ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage).ToList();
                    string modelStateErrors = string.Join("; ", errors);
                    _logger.LogWarning("GenerateSchema: ModelState không hợp lệ. Lỗi: {Errors}", modelStateErrors);
                    return Json(new { success = false, message = $"Dữ liệu không hợp lệ: {modelStateErrors}. Vui lòng kiểm tra lại các trường đã nhập." });
                }

                string jobId = Guid.NewGuid().ToString();

                var validTables = model.Tables?.Where(t => !string.IsNullOrWhiteSpace(t.TableName) && t.Properties != null && t.Properties.Any()).ToList()
                                 ?? new List<TableDefinitionViewModel>();

                if (!validTables.Any())
                {
                    _logger.LogWarning("GenerateSchema: Không có bảng hợp lệ nào được định nghĩa. Số lượng bảng nhận được: {TableCount}", model.Tables?.Count ?? 0);
                    JobStatusManager.SetJobStatus(jobId, JobStatusManager.StatusError, "Không có bảng hợp lệ nào được định nghĩa để tạo mã.", null);
                    return Json(new { success = false, message = "Không có bảng hợp lệ nào được định nghĩa để tạo mã. Vui lòng thêm ít nhất một bảng với tên và các thuộc tính." });
                }

                JobStatusManager.SetJobStatus(jobId, JobStatusManager.StatusQueued, "Đang chờ xử lý...", null);

                _ = Task.Run(async () =>
                {
                    try
                    {
                        JobStatusManager.SetJobStatus(jobId, JobStatusManager.StatusProcessing, "Đang phân tích và tạo mã...", null);
                        _logger.LogInformation("[Job {JobId}] Bắt đầu tạo mã cho Namespace: {RootNamespace}", jobId, model.RootNamespace);

                        string tempBaseDir = Path.Combine(Path.GetTempPath(), $"GeneratedCode_{jobId}");
                        if (Directory.Exists(tempBaseDir)) Directory.Delete(tempBaseDir, true);
                        Directory.CreateDirectory(tempBaseDir);

                        // Define directory structure - "Entities" changed to "Models"
                        string modelsDir = Path.Combine(tempBaseDir, "Models"); // Changed from entitiesDir
                        string dataDir = Path.Combine(tempBaseDir, "Data");
                        string repositoriesDir = Path.Combine(tempBaseDir, "Repositories");
                        string repoInterfacesDir = Path.Combine(repositoriesDir, "Interfaces");
                        string repoImplementationsDir = Path.Combine(repositoriesDir, "Implementations");
                        string servicesDir = Path.Combine(tempBaseDir, "Services");
                        string serviceInterfacesDir = Path.Combine(servicesDir, "Interfaces");
                        string serviceImplementationsDir = Path.Combine(servicesDir, "Implementations");
                        // string dtosDir = Path.Combine(tempBaseDir, "Dtos"); // DTOs are removed
                        string extensionsDir = Path.Combine(tempBaseDir, "Extensions");
                        string testsDir = Path.Combine(tempBaseDir, "Tests", $"{model.RootNamespace}.Tests");
                        string repoTestsDir = Path.Combine(testsDir, "Repositories");
                        string serviceTestsDir = Path.Combine(testsDir, "Services");

                        Directory.CreateDirectory(modelsDir); // Changed from entitiesDir
                        Directory.CreateDirectory(dataDir);
                        Directory.CreateDirectory(repoInterfacesDir);
                        Directory.CreateDirectory(repoImplementationsDir);

                        if (model.GenerateIServices || model.GenerateServices)
                        {
                            Directory.CreateDirectory(serviceInterfacesDir);
                            Directory.CreateDirectory(serviceImplementationsDir);
                            // Directory.CreateDirectory(dtosDir); // DTOs are removed
                        }
                        if (model.GenerateDependencyInjectionExtensions) Directory.CreateDirectory(extensionsDir);
                        if (model.GenerateUnitTests)
                        {
                            Directory.CreateDirectory(repoTestsDir);
                            if (model.GenerateServices) Directory.CreateDirectory(serviceTestsDir);
                        }

                        var allInverseRelationships = _codeGeneratorService.AnalyzeInverseRelationships(validTables, model.NamingConventions);
                        _logger.LogInformation("[Job {JobId}] Phân tích mối quan hệ hoàn tất.", jobId);

                        foreach (var table in validTables)
                        {
                            // GenerateModelClass instead of GenerateEntityClass
                            string modelCode = _codeGeneratorService.GenerateModelClass(table, model, allInverseRelationships);
                            // No EntityNameSuffix
                            string modelFileName = $"{_codeGeneratorService.ToPascalCase(table.TableName)}.cs"; 
                            await System.IO.File.WriteAllTextAsync(Path.Combine(modelsDir, modelFileName), modelCode); // Use modelsDir
                        }
                        _logger.LogInformation("[Job {JobId}] Sinh Models hoàn tất.", jobId);

                        string dbContextCode = _codeGeneratorService.GenerateDbContextClass(validTables, model, allInverseRelationships);
                        await System.IO.File.WriteAllTextAsync(Path.Combine(dataDir, $"{_codeGeneratorService.ToPascalCase(model.DbContextName)}.cs"), dbContextCode);
                        _logger.LogInformation("[Job {JobId}] Sinh DbContext hoàn tất.", jobId);

                        string genericIRepoCode = _codeGeneratorService.GenerateGenericIRepository(model);
                        await System.IO.File.WriteAllTextAsync(Path.Combine(repoInterfacesDir, "IRepository.cs"), genericIRepoCode);
                        string genericRepoCode = _codeGeneratorService.GenerateGenericRepositoryClass(model);
                        await System.IO.File.WriteAllTextAsync(Path.Combine(repoImplementationsDir, "Repository.cs"), genericRepoCode);
                        _logger.LogInformation("[Job {JobId}] Sinh Generic Repository hoàn tất.", jobId);

                        foreach (var table in validTables)
                        {
                            string modelNameBase = _codeGeneratorService.ToPascalCase(table.TableName); // No suffix

                            string iSpecRepoCode = _codeGeneratorService.GenerateSpecificIRepository(table, model);
                            string iSpecRepoFileName = $"{model.NamingConventions.RepositoryInterfacePrefix}{modelNameBase}{model.NamingConventions.RepositoryClassSuffix}.cs";
                            await System.IO.File.WriteAllTextAsync(Path.Combine(repoInterfacesDir, iSpecRepoFileName), iSpecRepoCode);

                            string specRepoCode = _codeGeneratorService.GenerateSpecificRepositoryClass(table, model);
                            string specRepoFileName = $"{modelNameBase}{model.NamingConventions.RepositoryClassSuffix}.cs";
                            await System.IO.File.WriteAllTextAsync(Path.Combine(repoImplementationsDir, specRepoFileName), specRepoCode);

                            if (model.GenerateIServices || model.GenerateServices)
                            {
                                // DTO generation is removed
                                // string dtoListCode = _codeGeneratorService.GenerateDtoClasses(table, model);
                                // string dtoListFileName = $"{modelNameBase}Dtos.cs"; 
                                // await System.IO.File.WriteAllTextAsync(Path.Combine(dtosDir, dtoListFileName), dtoListCode);

                                if (model.GenerateIServices)
                                {
                                    string iServiceCode = _codeGeneratorService.GenerateIServiceInterface(table, model);
                                    string iServiceFileName = $"{model.NamingConventions.ServiceInterfacePrefix}{modelNameBase}{model.NamingConventions.ServiceClassSuffix}.cs";
                                    await System.IO.File.WriteAllTextAsync(Path.Combine(serviceInterfacesDir, iServiceFileName), iServiceCode);
                                }
                                if (model.GenerateServices)
                                {
                                    string serviceCode = _codeGeneratorService.GenerateServiceClass(table, model);
                                    string serviceFileName = $"{modelNameBase}{model.NamingConventions.ServiceClassSuffix}.cs";
                                    await System.IO.File.WriteAllTextAsync(Path.Combine(serviceImplementationsDir, serviceFileName), serviceCode);
                                }
                            }

                            if (model.GenerateUnitTests)
                            {
                                string repoTestCode = _codeGeneratorService.GenerateRepositoryUnitTests(table, model);
                                string repoTestFileName = $"{modelNameBase}{model.NamingConventions.RepositoryClassSuffix}Tests.cs";
                                await System.IO.File.WriteAllTextAsync(Path.Combine(repoTestsDir, repoTestFileName), repoTestCode);

                                if (model.GenerateServices)
                                {
                                    string serviceTestCode = _codeGeneratorService.GenerateServiceUnitTests(table, model);
                                    string serviceTestFileName = $"{modelNameBase}{model.NamingConventions.ServiceClassSuffix}Tests.cs";
                                    await System.IO.File.WriteAllTextAsync(Path.Combine(serviceTestsDir, serviceTestFileName), serviceTestCode);
                                }
                            }
                        }
                        _logger.LogInformation("[Job {JobId}] Sinh các thành phần cụ thể (Repositories, Services, Tests) hoàn tất.", jobId);

                        if (model.GenerateDependencyInjectionExtensions)
                        {
                            string diCode = _codeGeneratorService.GenerateDependencyInjectionExtensions(validTables, model);
                            await System.IO.File.WriteAllTextAsync(Path.Combine(extensionsDir, "DependencyInjectionExtensions.cs"), diCode);
                            _logger.LogInformation("[Job {JobId}] Sinh Dependency Injection Extensions hoàn tất.", jobId);
                        }

                        JobStatusManager.SetJobStatus(jobId, JobStatusManager.StatusProcessing, "Đang nén file...", null);
                        await Task.Delay(500);

                        string zipFileName = $"GeneratedCode_{model.RootNamespace.Replace(".", "_")}_{jobId.Substring(0, 8)}.zip";
                        string zipFilePath = Path.Combine(Path.GetTempPath(), zipFileName);
                        if (System.IO.File.Exists(zipFilePath)) System.IO.File.Delete(zipFilePath);
                        ZipFile.CreateFromDirectory(tempBaseDir, zipFilePath);

                        JobStatusManager.SetJobStatus(jobId, JobStatusManager.StatusCompleted, "Mã đã được tạo thành công!", zipFileName);
                        _logger.LogInformation("[Job {JobId}] Hoàn tất tạo mã và tạo file ZIP: {ZipFileName}", jobId, zipFileName);

                        Directory.Delete(tempBaseDir, true);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "[Job {JobId}] Đã xảy ra lỗi nghiêm trọng trong tác vụ nền.", jobId);
                        JobStatusManager.SetJobStatus(jobId, JobStatusManager.StatusError, $"Lỗi trong quá trình tạo mã nền: {ex.Message}", null);
                    }
                });

                await Task.Yield();
                return Json(new { success = true, jobId = jobId, message = "Quá trình tạo mã đã được khởi tạo. Theo dõi trạng thái." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GenerateSchema: Lỗi không mong muốn trước khi bắt đầu tác vụ nền.");
                string errorJobId = Guid.NewGuid().ToString();
                JobStatusManager.SetJobStatus(errorJobId, JobStatusManager.StatusError, $"Lỗi máy chủ không mong muốn: {ex.Message}. Không thể bắt đầu tạo mã.", null);
                return Json(new { success = false, message = $"Lỗi máy chủ không mong muốn: {ex.Message}. Không thể bắt đầu tạo mã.", jobId = errorJobId });
            }
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
            if (string.IsNullOrWhiteSpace(fileName) || fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                 _logger.LogWarning("Yêu cầu tải file với tên không hợp lệ: {FileName}", fileName);
                return BadRequest("Tên file không hợp lệ.");
            }

            string filePath = Path.Combine(Path.GetTempPath(), fileName);
            if (!System.IO.File.Exists(filePath))
            {
                _logger.LogWarning("File không tồn tại để tải về: {FilePath}", filePath);
                return NotFound($"File '{fileName}' không tìm thấy hoặc đã bị xóa. Vui lòng thử tạo lại.");
            }
            
            try
            {
                var fileBytes = System.IO.File.ReadAllBytes(filePath);
                return File(fileBytes, "application/zip", fileName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi đọc file ZIP để tải về: {FilePath}", filePath);
                return StatusCode(500, "Lỗi khi đọc file ZIP.");
            }
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
