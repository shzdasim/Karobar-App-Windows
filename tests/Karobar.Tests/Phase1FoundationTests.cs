using System.IO.Compression;
using System.Text;
using FluentAssertions;
using Karobar.Application.Barcodes;
using Karobar.Application.Interfaces;
using Karobar.Domain.Constants;
using Karobar.Domain.Entities;
using Karobar.Domain.Formulas;
using Karobar.Infrastructure;
using Karobar.Infrastructure.Importing;
using Karobar.Infrastructure.Persistence;
using Karobar.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Karobar.Tests;

public sealed class Phase1FoundationTests
{
    [Fact]
    public void PurchaseInvoiceCalculator_ShouldSyncPackAndUnit_AndDiluteAvgCostWithBonus()
    {
        var item = new PurchaseLineCalcModel
        {
            PackSize = 10,
            PackQuantity = 2m,
            PackBonus = 1m,
            PackPurchasePrice = 300m,
            PackSalePrice = 400m,
            ItemDiscountPercentage = 10m
        };

        InvoiceCalculators.RecalcPurchaseItem(item, "pack_quantity");
        InvoiceCalculators.RecalcPurchaseItem(item, "pack_bonus");
        InvoiceCalculators.RecalcPurchaseItem(item, "pack_purchase_price");
        InvoiceCalculators.RecalcPurchaseItem(item, "pack_sale_price");

        item.UnitQuantity.Should().Be(20);
        item.UnitBonus.Should().Be(10);
        item.Quantity.Should().Be(30); // 20 paid + 10 free bonus
        item.UnitPurchasePrice.Should().Be(30m);
        item.UnitSalePrice.Should().Be(40m);
        item.SubTotal.Should().Be(540.00m); // 20 * 30 = 600 - 10% = 540
        item.AvgPrice.Should().Be(18.00m);  // 540 / 30 total units = 18.00
        item.Margin.Should().Be(55.00m);    // (40 - 18) / 40 * 100 = 55%
    }

    [Fact]
    public void SaleInvoiceCalculator_ShouldIntegerRoundTotal_AndSyncCashReceive()
    {
        var line = new SaleLineCalcModel
        {
            Quantity = 3,
            Price = 33.50m,
            ItemDiscountPercentage = 5m
        };
        InvoiceCalculators.RecalcRetailSaleItem(line);
        line.SubTotal.Should().Be(95.48m); // 100.50 - 5.025 = 95.475 -> 95.48

        var footer = new SaleFooterCalcModel
        {
            InvoiceType = "debit",
            DiscountPercentage = 0m,
            TaxPercentage = 0m,
            PreviousBalance = 200m
        };

        InvoiceCalculators.RecalcSaleFooter([line], footer);
        footer.Total.Should().Be(95m); // Rounded to nearest whole Rupee
        footer.TotalReceive.Should().Be(95m);
        footer.RemainThisBill.Should().Be(0m);
        footer.TotalBalance.Should().Be(200m);
    }

    [Fact]
    public async Task DatabaseInitializer_ShouldCreateSchema_Seed145Permissions_AndAuthenticateLaravelBcrypt()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "karobar_test_" + Guid.NewGuid().ToString("N"));
        try
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddKarobarInfrastructure(tempDir);

            await using var provider = services.BuildServiceProvider();
            var dbInit = provider.GetRequiredService<IDatabaseInitializerService>();
            var auth = provider.GetRequiredService<IAuthService>();

            await dbInit.InitializeAndSeedAsync();

            var stats = await dbInit.GetOverviewStatsAsync();
            stats.UsersCount.Should().Be(1);
            stats.RolesCount.Should().Be(DomainSeedConstants.DefaultRoleNames.Length);
            stats.PermissionsCount.Should().Be(DomainSeedConstants.AllPermissions.Length);
            stats.ChartOfAccountsCount.Should().Be(14);
            stats.StorageLocationsCount.Should().Be(3);

            // Verify login with built-in admin username ("admin") and password ("password")
            var session = await auth.LoginAsync("admin", "password");
            session.Username.Should().Be("admin");
            session.IsAdmin.Should().BeTrue();
            session.Permissions.Count.Should().Be(DomainSeedConstants.AllPermissions.Length);

            // Verify unsuccessful login throws clear error message
            var badLogin = async () => await auth.LoginAsync("admin", "wrong_password");
            await badLogin.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("Invalid username or password.");

            // Verify PHP $2y$ BCrypt compatibility
            var netHash = BCrypt.Net.BCrypt.HashPassword("secret123");
            var phpStyleHash = "$2y$" + netHash[4..];
            AuthService.VerifyBcryptHash("secret123", phpStyleHash).Should().BeTrue();
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, recursive: true); } catch { }
            }
        }
    }

    [Fact]
    public async Task LegacyKarobarImporter_ShouldImportFullBackupZip_AndSqlStatements()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "karobar_imp_test_" + Guid.NewGuid().ToString("N"));
        var zipPath = Path.Combine(Path.GetTempPath(), $"full_backup_{Guid.NewGuid():N}.zip");

        try
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddKarobarInfrastructure(tempDir);

            await using var provider = services.BuildServiceProvider();
            var importer = provider.GetRequiredService<ILegacyKarobarImporterService>();

            // Build a sample Karobar Full Backup .zip with database/database.sql.gz and settings.json
            var sampleSql = """
                -- Karobar SQL Dump
                INSERT INTO `categories` (`id`, `name`) VALUES (10, 'Antibiotics');
                INSERT INTO `brands` (`id`, `name`, `expiry_return_policy`, `expiry_return_months`) VALUES (20, 'GSK Pharma', 'before_expiry', 6);
                INSERT INTO `products` (`id`, `product_code`, `name`, `formulation`, `pack_size`, `quantity`, `pack_purchase_price`, `unit_purchase_price`, `pack_sale_price`, `unit_sale_price`, `avg_price`, `narcotic`, `category_id`, `brand_id`)
                VALUES (100, 'PRD-0100', 'Augmentin 625mg Tabs', 'Co-Amoxiclav', 14, 140, 350.00, 25.00, 420.00, 30.00, 25.00, 'no', 10, 20);
                """;

            await using (var fs = File.Create(zipPath))
            using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
            {
                var dbEntry = zip.CreateEntry("database/database.sql.gz");
                await using (var entryStream = dbEntry.Open())
                await using (var gzip = new GZipStream(entryStream, CompressionLevel.Fastest))
                {
                    var bytes = Encoding.UTF8.GetBytes(sampleSql);
                    await gzip.WriteAsync(bytes);
                }

                var settingsEntry = zip.CreateEntry("settings.json");
                await using (var sStream = settingsEntry.Open())
                {
                    var jsonBytes = Encoding.UTF8.GetBytes("""{"data":{"store_name":"Al-Shifa Pharmacy","shop_type":"pharmacy","currency_symbol":"Rs."}}""");
                    await sStream.WriteAsync(jsonBytes);
                }
            }

            var report = await importer.ImportFromFileAsync(zipPath);
            report.Success.Should().BeTrue(report.Message);
            report.PostImportStats.Should().NotBeNull();
            report.PostImportStats!.StoreName.Should().Be("Al-Shifa Pharmacy");
            report.PostImportStats.ProductsCount.Should().Be(1);
            report.PostImportStats.TotalUnitsInStock.Should().Be(140);
            report.PostImportStats.InventoryCostValue.Should().Be(3500.00m);
            report.PostImportStats.InventoryRetailValue.Should().Be(4200.00m);
        }
        finally
        {
            if (File.Exists(zipPath)) try { File.Delete(zipPath); } catch { }
            if (Directory.Exists(tempDir)) try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }

    [Fact]
    public void LicenseService_ShouldFormatMachineId_AndRejectInvalidKey()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "karobar_lic_test_" + Guid.NewGuid().ToString("N"));
        try
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddKarobarInfrastructure(tempDir);

            using var provider = services.BuildServiceProvider();
            var license = provider.GetRequiredService<ILicenseService>();

            var mid = license.ComputeMachineId();
            mid.Should().MatchRegex(@"^[A-F0-9]{4}-[A-F0-9]{4}-[A-F0-9]{4}-[A-F0-9]{4}$");

            var check = license.VerifyKey("KRBL.invalid.key", mid);
            check.Valid.Should().BeFalse();
        }
        finally
        {
            if (Directory.Exists(tempDir)) try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task CategoryService_ShouldSupportCrud_ProductGuard_AndCsvImportExport()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "karobar_cat_test_" + Guid.NewGuid().ToString("N"));
        var csvPath = Path.Combine(Path.GetTempPath(), $"cat_import_{Guid.NewGuid():N}.csv");
        var exportPath = Path.Combine(Path.GetTempPath(), $"cat_export_{Guid.NewGuid():N}.csv");

        try
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddKarobarInfrastructure(tempDir);

            await using var provider = services.BuildServiceProvider();
            var dbInit = provider.GetRequiredService<IDatabaseInitializerService>();
            var categories = provider.GetRequiredService<ICategoryService>();

            await dbInit.InitializeAndSeedAsync();

            // 1. Create & Duplicate check
            var c1 = await categories.CreateAsync("Antibiotics");
            c1.Name.Should().Be("Antibiotics");

            var dupAct = async () => await categories.CreateAsync("antibiotics");
            await dupAct.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("The name has already been taken.");

            // 2. Update
            var updated = await categories.UpdateAsync(c1.Id, "Broad Antibiotics");
            updated.Name.Should().Be("Broad Antibiotics");

            // 3. CSV Validate & Commit
            await File.WriteAllTextAsync(csvPath, "name\nAnalgesics\nVitamins\n\nAnalgesics\n");
            var valRes = await categories.ValidateCsvAsync(csvPath, ",");
            valRes.Ok.Should().BeTrue();
            valRes.Total.Should().Be(3);
            valRes.Valid.Should().Be(2);
            valRes.Invalid.Should().Be(1); // Duplicate Analgesics in file

            var commitStats = await categories.CommitCsvAsync(csvPath, insertValidOnly: true, ",");
            commitStats.NewCount.Should().Be(2);

            var all = await categories.GetAllAsync();
            all.Select(x => x.Name).Should().Contain(["Broad Antibiotics", "Analgesics", "Vitamins"]);

            // 4. Export CSV
            await categories.ExportCsvAsync(exportPath);
            var exportedText = await File.ReadAllTextAsync(exportPath);
            exportedText.Should().Contain("Analgesics");
            exportedText.Should().Contain("Broad Antibiotics");

            // 5. Delete
            await categories.DeleteAsync(c1.Id);
            (await categories.GetAllAsync()).Select(x => x.Name).Should().NotContain("Broad Antibiotics");
        }
        finally
        {
            if (File.Exists(csvPath)) try { File.Delete(csvPath); } catch { }
            if (File.Exists(exportPath)) try { File.Delete(exportPath); } catch { }
            if (Directory.Exists(tempDir)) try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task BrandService_ShouldSupportCrud_ExpiryPolicies_BatchIntimation_AndCsvImportExport()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "karobar_brand_test_" + Guid.NewGuid().ToString("N"));
        var csvPath = Path.Combine(Path.GetTempPath(), $"brand_import_{Guid.NewGuid():N}.csv");
        var exportPath = Path.Combine(Path.GetTempPath(), $"brand_export_{Guid.NewGuid():N}.csv");

        try
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddKarobarInfrastructure(tempDir);

            await using var provider = services.BuildServiceProvider();
            var dbInit = provider.GetRequiredService<IDatabaseInitializerService>();
            var brands = provider.GetRequiredService<IBrandService>();

            await dbInit.InitializeAndSeedAsync();

            // 1. Create Brand with 6 Months Before Expiry policy
            var b1 = await brands.CreateAsync(new BrandUpsertRequest
            {
                Name = "GSK",
                ExpiryReturnPolicy = "before_expiry",
                ExpiryReturnMonths = 6,
                ExpiryReturnDays = 180,
                ExpiryReturnNotes = "Intimate distributor 180 days prior"
            });

            b1.Name.Should().Be("GSK");
            b1.ExpiryReturnPolicy.Should().Be("before_expiry");
            b1.ExpiryReturnMonths.Should().Be(6);
            b1.ExpiryReturnDays.Should().Be(180);
            b1.ExpiryPolicyLabel.Should().Be("6 Months Before Expiry");

            // Duplicate check (case-insensitive)
            var dupAct = async () => await brands.CreateAsync(new BrandUpsertRequest { Name = "gsk" });
            await dupAct.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("The name has already been taken.");

            // 2. Update Brand to Accepts Expired Products
            var updated = await brands.UpdateAsync(b1.Id, new BrandUpsertRequest
            {
                Name = "GlaxoSmithKline",
                ExpiryReturnPolicy = "accepts_expired",
                ExpiryReturnNotes = "Accepts expired packs"
            });

            updated.Name.Should().Be("GlaxoSmithKline");
            updated.ExpiryReturnPolicy.Should().Be("accepts_expired");
            updated.ExpiryReturnMonths.Should().Be(0);
            updated.ExpiryReturnDays.Should().Be(0);
            updated.ExpiryPolicyLabel.Should().Be("Accepts Expired Products");

            // 3. CSV Validate & Commit
            await File.WriteAllTextAsync(csvPath, "name,image\nAbbott,brands/abbott.png\nGetz Pharma,https://example.com/getz.png\nAbbott,dup.png\n");
            var valRes = await brands.ValidateCsvAsync(csvPath, ",");
            valRes.Ok.Should().BeTrue();
            valRes.Total.Should().Be(3);
            valRes.Valid.Should().Be(2);
            valRes.Invalid.Should().Be(1);

            var commitStats = await brands.CommitCsvAsync(csvPath, insertValidOnly: true, ",");
            commitStats.NewCount.Should().Be(2);

            var all = await brands.GetAllAsync();
            all.Select(x => x.Name).Should().Contain(["GlaxoSmithKline", "Abbott", "Getz Pharma"]);

            // 4. Export CSV
            await brands.ExportCsvAsync(exportPath);
            var exportedText = await File.ReadAllTextAsync(exportPath);
            exportedText.Should().Contain("GlaxoSmithKline");
            exportedText.Should().Contain("Abbott");

            // 5. Delete
            await brands.DeleteAsync(b1.Id);
            (await brands.GetAllAsync()).Select(x => x.Name).Should().NotContain("GlaxoSmithKline");
        }
        finally
        {
            if (File.Exists(csvPath)) try { File.Delete(csvPath); } catch { }
            if (File.Exists(exportPath)) try { File.Delete(exportPath); } catch { }
            if (Directory.Exists(tempDir)) try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task SupplierService_ShouldSupportCrud_OpeningBalanceLedger_CreditSummary_AndCsvImportExport()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "karobar_sup_test_" + Guid.NewGuid().ToString("N"));
        var csvPath = Path.Combine(Path.GetTempPath(), $"sup_import_{Guid.NewGuid():N}.csv");
        var exportPath = Path.Combine(Path.GetTempPath(), $"sup_export_{Guid.NewGuid():N}.csv");

        try
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddKarobarInfrastructure(tempDir);

            await using var provider = services.BuildServiceProvider();
            var dbInit = provider.GetRequiredService<IDatabaseInitializerService>();
            var suppliers = provider.GetRequiredService<ISupplierService>();

            await dbInit.InitializeAndSeedAsync();

            var s1 = await suppliers.CreateAsync(new SupplierUpsertRequest
            {
                Name = "Muller & Phipps",
                Phone = "0300-1112233",
                Address = "Industrial Area",
                TaxStatus = "registered",
                TaxNumber = "NTN-123456",
                CreditLimit = 100000m,
                CreditDays = 45,
                OpeningBalance = 25000m,
                ExpiryReturnPolicy = "before_expiry",
                ExpiryReturnMonths = 6,
                ExpiryReturnDays = 180
            });

            s1.Name.Should().Be("Muller & Phipps");
            s1.CurrentPayable.Should().Be(25000m);
            s1.ExpiryPolicyLabel.Should().Be("6 Months Before Expiry");

            var credit = await suppliers.GetCreditSummaryAsync(s1.Id);
            credit.CurrentBalance.Should().Be(25000m);
            credit.AvailableCredit.Should().Be(75000m);
            credit.IsLimitExceeded.Should().BeFalse();

            var updated = await suppliers.UpdateAsync(s1.Id, new SupplierUpsertRequest
            {
                Name = "M&P Pakistan",
                Phone = "0300-1112233",
                Address = "Lahore",
                CreditLimit = 20000m,
                CreditDays = 30,
                ExpiryReturnPolicy = "accepts_expired"
            });
            updated.Name.Should().Be("M&P Pakistan");
            updated.ExpiryReturnPolicy.Should().Be("accepts_expired");

            var creditAfter = await suppliers.GetCreditSummaryAsync(s1.Id);
            creditAfter.IsLimitExceeded.Should().BeTrue(); // 25,000 > 20,000 limit

            await File.WriteAllTextAsync(csvPath, "name,address,phone,opening_balance\nPremier Distributors,Karachi,0321-0001111,12000\nUDL Distribution,Islamabad,0333-2223333,0\n");
            var valRes = await suppliers.ValidateCsvAsync(csvPath, ",");
            valRes.Ok.Should().BeTrue();
            valRes.Valid.Should().Be(2);

            var stats = await suppliers.CommitCsvAsync(csvPath, insertValidOnly: true, ",");
            stats.NewCount.Should().Be(2);

            await suppliers.ExportCsvAsync(exportPath);
            var exported = await File.ReadAllTextAsync(exportPath);
            exported.Should().Contain("M&P Pakistan");
            exported.Should().Contain("Premier Distributors");

            await suppliers.DeleteAsync(s1.Id);
            (await suppliers.GetAllAsync()).Select(x => x.Name).Should().NotContain("M&P Pakistan");
        }
        finally
        {
            if (File.Exists(csvPath)) try { File.Delete(csvPath); } catch { }
            if (File.Exists(exportPath)) try { File.Delete(exportPath); } catch { }
            if (Directory.Exists(tempDir)) try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task CustomerService_ShouldSupportCrud_RegularRefillReminders_AndCsvImportExport()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "karobar_cust_test_" + Guid.NewGuid().ToString("N"));
        var csvPath = Path.Combine(Path.GetTempPath(), $"cust_import_{Guid.NewGuid():N}.csv");
        var exportPath = Path.Combine(Path.GetTempPath(), $"cust_export_{Guid.NewGuid():N}.csv");

        try
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddKarobarInfrastructure(tempDir);

            await using var provider = services.BuildServiceProvider();
            var dbInit = provider.GetRequiredService<IDatabaseInitializerService>();
            var customers = provider.GetRequiredService<ICustomerService>();

            await dbInit.InitializeAndSeedAsync();

            var c1 = await customers.CreateAsync(new CustomerUpsertRequest
            {
                Name = "Tariq Mehmood",
                Phone = "0300-1234567",
                Email = "tariq@example.com",
                Address = "Model Town",
                CreditLimit = 50000m,
                CreditDays = 30,
                CreditStatus = "active",
                OpeningBalance = 4500m,
                IsRegular = true,
                RefillCycle = "monthly",
                ReminderLeadDays = 3,
                NextRefillDate = DateTime.Today.AddDays(2).ToString("yyyy-MM-dd"),
                ReminderLanguage = "urdu",
                RegularNotes = "Blood pressure & diabetes monthly pack"
            });

            c1.Name.Should().Be("Tariq Mehmood");
            c1.IsRegular.Should().BeTrue();
            c1.IsDueForReminder.Should().BeTrue();
            c1.CurrentReceivable.Should().Be(4500m);

            var preview = await customers.GetRefillReminderPreviewAsync(c1.Id);
            preview.NormalizedPhone.Should().Be("923001234567");
            preview.UrduMessage.Should().Contain("Tariq Mehmood");
            preview.EnglishMessage.Should().Contain("Tariq Mehmood");

            var (waUrl, afterRemind) = await customers.RecordRefillReminderSentAsync(
                c1.Id,
                "0300-1234567",
                "urdu",
                preview.UrduMessage,
                savePhone: true,
                advanceCycle: true);

            waUrl.Should().StartWith("https://wa.me/923001234567?text=");
            afterRemind.HasLastReminded.Should().BeTrue();

            await File.WriteAllTextAsync(csvPath, "name,email,phone,address,opening_balance\nSana Medical,sana@med.pk,0321-9998887,Gulberg,8000\n");
            var valRes = await customers.ValidateCsvAsync(csvPath, ",");
            valRes.Ok.Should().BeTrue();
            valRes.Valid.Should().Be(1);

            var stats = await customers.CommitCsvAsync(csvPath, insertValidOnly: true, ",");
            stats.NewCount.Should().Be(1);

            await customers.ExportCsvAsync(exportPath);
            var exported = await File.ReadAllTextAsync(exportPath);
            exported.Should().Contain("Tariq Mehmood");
            exported.Should().Contain("Sana Medical");

            await customers.DeleteAsync(c1.Id);
            (await customers.GetAllAsync()).Select(x => x.Name).Should().NotContain("Tariq Mehmood");
        }
        finally
        {
            if (File.Exists(csvPath)) try { File.Delete(csvPath); } catch { }
            if (File.Exists(exportPath)) try { File.Delete(exportPath); } catch { }
            if (Directory.Exists(tempDir)) try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task ProductService_ShouldSupportCrud_BatchesFefo_StorageLocations_MultiSupplier_Substitutes_AndCsvImportExport()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "karobar_prod_test_" + Guid.NewGuid().ToString("N"));
        var csvPath = Path.Combine(Path.GetTempPath(), $"prod_import_{Guid.NewGuid():N}.csv");

        try
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddKarobarInfrastructure(tempDir);

            await using var provider = services.BuildServiceProvider();
            var dbInit = provider.GetRequiredService<IDatabaseInitializerService>();
            var categories = provider.GetRequiredService<ICategoryService>();
            var brands = provider.GetRequiredService<IBrandService>();
            var suppliers = provider.GetRequiredService<ISupplierService>();
            var products = provider.GetRequiredService<IProductService>();

            await dbInit.InitializeAndSeedAsync();

            var cat = await categories.CreateAsync("Analgesics");
            var br1 = await brands.CreateAsync(new BrandUpsertRequest { Name = "GSK" });
            var br2 = await brands.CreateAsync(new BrandUpsertRequest { Name = "Getz Pharma" });
            var sup1 = await suppliers.CreateAsync(new SupplierUpsertRequest { Name = "M&P Distributors", Phone = "0300-1112222" });
            var sup2 = await suppliers.CreateAsync(new SupplierUpsertRequest { Name = "Premier Pharma", Phone = "0321-3334444" });

            var codes = await products.GenerateNewCodeAndBarcodeAsync();
            codes.ProductCode.Should().StartWith("PRD-");
            codes.Barcode.Should().StartWith("PRD");

            var createRes = await products.CreateProductAsync(new ProductUpsertRequest
            {
                ProductCode = codes.ProductCode,
                Barcode = codes.Barcode,
                Name = "Panadol 500mg Tabs",
                Formulation = "Paracetamol 500mg",
                PackSize = 10,
                Quantity = 100,
                PackPurchasePrice = 35m,
                UnitPurchasePrice = 3.5m,
                PackSalePrice = 50m,
                UnitSalePrice = 5.0m,
                AvgPrice = 3.5m,
                Margin = 30m,
                CategoryId = cat.Id,
                BrandId = br1.Id,
                SupplierId = sup1.Id,
                Rack = "A-1"
            });

            createRes.Succeeded.Should().BeTrue();
            var p1Id = createRes.EntityId!.Value;

            // Allocate 60 units to Batch B-001 (expiring in 6 months) and 40 units to Batch B-002 (expiring in 3 months)
            var b1Res = await products.SaveProductBatchAsync(p1Id, new BatchSaveRequest
            {
                BatchNumber = "B-001",
                ExpiryDate = DateTime.Today.AddMonths(6),
                Quantity = 60,
                Rack = "A-1"
            });
            b1Res.Succeeded.Should().BeTrue();

            var b2Res = await products.SaveProductBatchAsync(p1Id, new BatchSaveRequest
            {
                BatchNumber = "B-002",
                ExpiryDate = DateTime.Today.AddMonths(3),
                Quantity = 40,
                Rack = "A-1"
            });
            b2Res.Succeeded.Should().BeTrue();

            // FEFO check: B-002 expires sooner (3 months) so it should be the FEFO recommended batch
            var batchSummary = await products.GetProductBatchesAsync(p1Id);
            batchSummary.TotalStock.Should().Be(100);
            batchSummary.BatchedStock.Should().Be(100);
            batchSummary.UnassignedStock.Should().Be(0);
            batchSummary.Batches.First().BatchNumber.Should().Be("B-002");
            batchSummary.Batches.First().IsFefoRecommended.Should().BeTrue();

            // Product Batches Workbench Search & Global Directory checks
            var wbSearch = await products.SearchProductsForBatchWorkbenchAsync("Panadol", 10);
            wbSearch.Should().ContainSingle(p => p.Id == p1Id && p.Quantity == 100 && p.BatchedQuantity == 100 && p.UnassignedQuantity == 0);

            var globalDir = await products.GetGlobalBatchesDirectoryAsync(new GlobalBatchQueryFilter
            {
                Status = "all",
                IncludeZeroStock = true,
                Page = 1,
                PerPage = 25
            });
            globalDir.TotalCount.Should().BeGreaterThanOrEqualTo(2);
            globalDir.Summary.TotalBatchesTracked.Should().BeGreaterThanOrEqualTo(2);
            globalDir.Items.Should().Contain(b => b.ProductId == p1Id && b.BatchNumber == "B-001");
            globalDir.Items.Should().Contain(b => b.ProductId == p1Id && b.BatchNumber == "B-002");

            // Link secondary supplier
            var supLinkRes = await products.SaveProductSupplierAsync(p1Id, new ProductSupplierSaveRequest
            {
                SupplierId = sup2.Id,
                SupplierSku = "PREM-PAN-500",
                LastPurchasePrice = 3.4m,
                LeadTimeDays = 2,
                MinOrderQty = 5,
                IsPreferred = false
            });
            supLinkRes.Succeeded.Should().BeTrue();

            var linkedSuppliers = await products.GetProductSuppliersAsync(p1Id);
            linkedSuppliers.Should().HaveCount(2);

            // Create substitute product with same formulation
            var codes2 = await products.GenerateNewCodeAndBarcodeAsync();
            var subCreate = await products.CreateProductAsync(new ProductUpsertRequest
            {
                ProductCode = codes2.ProductCode,
                Barcode = codes2.Barcode,
                Name = "Calpol 500mg Tabs",
                Formulation = "Paracetamol 500mg",
                PackSize = 10,
                Quantity = 50,
                PackPurchasePrice = 30m,
                UnitPurchasePrice = 3.0m,
                PackSalePrice = 45m,
                UnitSalePrice = 4.5m,
                AvgPrice = 3.0m,
                CategoryId = cat.Id,
                BrandId = br2.Id,
                SupplierId = sup2.Id
            });
            subCreate.Succeeded.Should().BeTrue();

            var subLookup = await products.GetSubstituteProductsAsync(p1Id);
            subLookup.MatchType.Should().Be("formulation");
            subLookup.Substitutes.Should().ContainSingle(s => s.Name == "Calpol 500mg Tabs" && s.IsExactFormulation);

            // CSV Import & Export
            await File.WriteAllTextAsync(csvPath,
                "product_code,name,pack_size,unit_purchase_price,unit_sale_price,category,brand,supplier,quantity,barcode,narcotic,formulation\n" +
                "PRD-0999,Augmentin 625mg,6,40,55,Antibiotics,GSK,M&P Distributors,30,8999000111222,no,Co-Amoxiclav 625mg\n");

            var csvVal = await products.ValidateCsvImportAsync(csvPath, ",", createMissingRefs: true);
            csvVal.Ok.Should().BeTrue();
            csvVal.ValidRows.Should().Be(1);

            var csvCommit = await products.CommitCsvImportAsync(csvPath, insertValidOnly: true, ",", createMissingRefs: true);
            csvCommit.Succeeded.Should().BeTrue();
            csvCommit.NewCount.Should().Be(1);

            var csvExport = await products.ExportProductsCsvAsync();
            csvExport.Should().Contain("Panadol 500mg Tabs");
            csvExport.Should().Contain("Augmentin 625mg");
        }
        finally
        {
            if (File.Exists(csvPath)) try { File.Delete(csvPath); } catch { }
            if (Directory.Exists(tempDir)) try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task BarcodeStudioService_And_Code128Encoder_ShouldSupportTemplates_FefoQueueLookup_AndScanner()
    {
        // 1. Pure Vector Code 128 Encoder Verification
        var numericValues = Code128Encoder.EncodeValues("896400012345");
        numericValues.First().Should().Be(105); // Start Mode C (4+ leading digits)
        numericValues.Last().Should().Be(106);  // Stop pattern

        var alphaGeo = Code128Encoder.BuildGeometry("PRD-0042", barWidth: 1.3, barHeight: 28, quietZoneModules: 4);
        alphaGeo.CleanText.Should().Be("PRD-0042");
        alphaGeo.Bars.Should().NotBeEmpty();
        alphaGeo.TotalWidth.Should().BeGreaterThan(40);

        // 2. BarcodeStudioService Integration Verification
        var tempDir = Path.Combine(Path.GetTempPath(), "karobar_barcode_test_" + Guid.NewGuid().ToString("N"));
        try
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddKarobarInfrastructure(tempDir);

            await using var provider = services.BuildServiceProvider();
            var dbInit = provider.GetRequiredService<IDatabaseInitializerService>();
            var categories = provider.GetRequiredService<ICategoryService>();
            var brands = provider.GetRequiredService<IBrandService>();
            var suppliers = provider.GetRequiredService<ISupplierService>();
            var products = provider.GetRequiredService<IProductService>();
            var barcodes = provider.GetRequiredService<IBarcodeStudioService>();

            await dbInit.InitializeAndSeedAsync();

            var cat = await categories.CreateAsync("NSAIDs");
            var br = await brands.CreateAsync(new BrandUpsertRequest { Name = "Abbott" });
            var sup = await suppliers.CreateAsync(new SupplierUpsertRequest { Name = "UDL Distribution" });

            var boot = await barcodes.GetStudioBootstrapAsync();
            boot.Templates.Should().HaveCountGreaterThanOrEqualTo(6);
            boot.Templates.Should().Contain(t => t.IsDefault);

            // Save custom preset
            var customPreset = await barcodes.SaveTemplateAsync(new BarcodeTemplateSaveRequest
            {
                Name = "Pharmacy 45x30mm Custom",
                PaperType = "roll",
                WidthMm = 45m,
                HeightMm = 30m,
                Orientation = "landscape",
                IsDefault = true,
                Options = new BarcodeLayoutOptionsDto
                {
                    ShowStoreName = true,
                    StoreNameOverride = "Al-Shifa Pharmacy",
                    ShowBatch = true,
                    ShowExpiry = true,
                    ExpiryFormat = "DD-MM-YYYY",
                    PricePrefix = "MRP:"
                }
            });
            customPreset.IsDefault.Should().BeTrue();
            customPreset.Options.StoreNameOverride.Should().Be("Al-Shifa Pharmacy");
            customPreset.Options.ExpiryFormat.Should().Be("DD-MM-YYYY");

            // Create product with 2 batches + unassigned stock
            var prodCreate = await products.CreateProductAsync(new ProductUpsertRequest
            {
                ProductCode = "PRD-0042",
                Barcode = "896400099988",
                Name = "Brufen 400mg Tablets",
                PackSize = 30,
                Quantity = 100,
                PackPurchasePrice = 150m,
                UnitPurchasePrice = 5m,
                PackSalePrice = 210m,
                UnitSalePrice = 7m,
                AvgPrice = 5m,
                CategoryId = cat.Id,
                BrandId = br.Id,
                SupplierId = sup.Id
            });
            prodCreate.Succeeded.Should().BeTrue(prodCreate.Message);
            var prodId = prodCreate.EntityId!.Value;

            await products.SaveProductBatchAsync(prodId, new BatchSaveRequest
            {
                BatchNumber = "BR-LATE",
                ExpiryDate = DateTime.Today.AddMonths(10),
                Quantity = 40
            });
            await products.SaveProductBatchAsync(prodId, new BatchSaveRequest
            {
                BatchNumber = "BR-FEFO",
                ExpiryDate = DateTime.Today.AddMonths(3),
                Quantity = 35
            });

            var searchRes = await barcodes.SearchProductsAsync("Brufen");
            searchRes.Should().ContainSingle(p => p.Id == prodId);
            var lookup = searchRes.Single(p => p.Id == prodId);
            lookup.UnassignedQuantity.Should().Be(25); // 100 - (40 + 35)
            lookup.Batches.First().BatchNumber.Should().Be("BR-FEFO");
            lookup.Batches.First().IsFefo.Should().BeTrue();

            // Scanner lookup & weight-embedded EAN-13 lookup (20 00042 00750 5 -> 0.750kg of PRD-0042)
            var directScan = await barcodes.ScanProductAsync("896400099988");
            directScan.Found.Should().BeTrue();
            directScan.SelectedBatch?.BatchNumber.Should().Be("BR-FEFO");

            var weightScan = await barcodes.ScanProductAsync("2000042007505");
            weightScan.Found.Should().BeTrue();
            weightScan.IsWeightBarcode.Should().BeTrue();
            weightScan.WeightGrams.Should().Be(750);
            weightScan.ParsedQuantity.Should().Be(0.75m);
        }
        finally
        {
            if (Directory.Exists(tempDir)) try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task PurchaseInvoiceService_ShouldSupportCreate_WeightedAvg_PoReceiving_CreditLedger_CashDrawer_Drafts_Update_Reverse_AndDelete()
    {
        // 1. Verify Retail -> Wholesale Smart Sync in InvoiceCalculators
        var smartLine = new PurchaseLineCalcModel
        {
            PackSize = 10,
            PackQuantity = 1m,
            PackPurchasePrice = 100m,
            PackSalePrice = 150m,
            WholeSalePackPrice = 150m,
            WholeSaleUnitPrice = 15m
        };
        InvoiceCalculators.RecalcPurchaseItem(smartLine, "pack_size");
        smartLine.PackSalePrice = 180m;
        InvoiceCalculators.RecalcPurchaseItem(smartLine, "pack_sale_price", previousPackSalePrice: 150m);
        smartLine.WholeSalePackPrice.Should().Be(180m);
        smartLine.WholeSaleUnitPrice.Should().Be(18m);

        // 2. End-to-End PurchaseInvoiceService Integration Verification
        var tempDir = Path.Combine(Path.GetTempPath(), "karobar_pur_inv_test_" + Guid.NewGuid().ToString("N"));
        try
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddKarobarInfrastructure(tempDir);

            await using var provider = services.BuildServiceProvider();
            var dbInit = provider.GetRequiredService<IDatabaseInitializerService>();
            var auth = provider.GetRequiredService<IAuthService>();
            var categories = provider.GetRequiredService<ICategoryService>();
            var brands = provider.GetRequiredService<IBrandService>();
            var suppliers = provider.GetRequiredService<ISupplierService>();
            var products = provider.GetRequiredService<IProductService>();
            var purchases = provider.GetRequiredService<IPurchaseInvoiceService>();
            var dbFactory = provider.GetRequiredService<IDbContextFactory<KarobarDbContext>>();

            await dbInit.InitializeAndSeedAsync();
            var session = await auth.LoginAsync("admin", "password");

            var cat = await categories.CreateAsync("Antibiotics");
            var br = await brands.CreateAsync(new BrandUpsertRequest { Name = "GSK" });
            var sup = await suppliers.CreateAsync(new SupplierUpsertRequest
            {
                Name = "Premier Pharma",
                Phone = "0300-5556677",
                CreditDays = 30,
                CreditLimit = 100000m
            });

            // Initial product: 10 units @ Rs. 10.00 avg cost (Rs. 100.00 total inventory value)
            var prodRes = await products.CreateProductAsync(new ProductUpsertRequest
            {
                ProductCode = "PRD-AUG625",
                Barcode = "896400077711",
                Name = "Augmentin 625mg Tablets",
                PackSize = 10,
                Quantity = 10,
                PackPurchasePrice = 100m,
                UnitPurchasePrice = 10m,
                PackSalePrice = 150m,
                UnitSalePrice = 15m,
                WholeSalePackPrice = 140m,
                WholeSaleUnitPrice = 14m,
                AvgPrice = 10m,
                CategoryId = cat.Id,
                BrandId = br.Id,
                SupplierId = sup.Id
            });
            prodRes.Succeeded.Should().BeTrue(prodRes.Message);
            var prodId = prodRes.EntityId!.Value;

            // Open Cash Drawer Session (Rs. 50,000 opening balance) + Seed Ordered Purchase Order (30 units)
            long poId;
            await using (var setupDb = await dbFactory.CreateDbContextAsync())
            {
                var openedAt = DateTime.UtcNow.AddMinutes(-15);
                setupDb.CashDrawerSessions.Add(new CashDrawerSession
                {
                    OpenedBy = session.UserId,
                    OpenedAt = openedAt,
                    OpeningBalance = 50000m,
                    ExpectedClosingBalance = 50000m,
                    Status = "open",
                    CreatedAt = openedAt,
                    UpdatedAt = openedAt
                });

                var po = new PurchaseOrder
                {
                    OrderNumber = "PO-0001",
                    SupplierId = sup.Id,
                    OrderDate = DateTime.Today,
                    Status = "ordered",
                    Subtotal = 300m,
                    Total = 300m,
                    CreatedBy = session.UserId,
                    CreatedAt = openedAt,
                    UpdatedAt = openedAt
                };
                setupDb.PurchaseOrders.Add(po);
                await setupDb.SaveChangesAsync();
                poId = po.Id;

                setupDb.PurchaseOrderItems.Add(new PurchaseOrderItem
                {
                    PurchaseOrderId = po.Id,
                    ProductId = prodId,
                    PackSize = 10,
                    QuantityOrdered = 30,
                    QuantityReceived = 0,
                    UnitCost = 10m,
                    LineTotal = 300m,
                    Position = 1,
                    CreatedAt = openedAt,
                    UpdatedAt = openedAt
                });
                await setupDb.SaveChangesAsync();
            }

            // Prepare PO Receipt & Verify Bootstrap + Name Prefix Search (StartsWith, not Contains)
            var poPrep = await purchases.PreparePurchaseOrderReceiptAsync(poId);
            poPrep.OrderNumber.Should().Be("PO-0001");
            poPrep.Lines.Should().ContainSingle(l => l.ProductId == prodId && l.UnitQuantity == 30);

            var prefixMatches = await purchases.SearchProductsForPurchaseAsync("Aug", sup.Id);
            prefixMatches.Should().ContainSingle(p => p.Id == prodId && p.SupplierName == "Premier Pharma");

            var midWordMatches = await purchases.SearchProductsForPurchaseAsync("mentin", sup.Id);
            midWordMatches.Should().BeEmpty();

            // Hold Purchase Draft -> DRF-PUR-0001
            var draftPayload = new PurchaseInvoiceUpsertRequest
            {
                PurchaseOrderId = poId,
                ReceiptKey = poPrep.ReceiptKey,
                SupplierId = sup.Id,
                InvoiceType = "debit",
                PaymentMethod = "cash",
                PostedDate = DateTime.Today,
                InvoiceNumber = "SUP-BILL-101",
                InvoiceAmount = 200m,
                TotalAmount = 200m,
                Items =
                [
                    new PurchaseInvoiceUpsertLineRequest
                    {
                        ProductId = prodId,
                        PackSize = 10,
                        Batch = "AUG-B1",
                        Expiry = DateTime.Today.AddMonths(12),
                        PackQuantity = 2m,
                        UnitQuantity = 20,
                        PackBonus = 0.5m,
                        UnitBonus = 5,
                        PackPurchasePrice = 100m,
                        UnitPurchasePrice = 10m,
                        PackSalePrice = 150m,
                        UnitSalePrice = 15m,
                        WholeSalePackPrice = 140m,
                        WholeSaleUnitPrice = 14m
                    }
                ]
            };

            var draft1 = await purchases.SaveDraftAsync(null, draftPayload, session.UserId);
            draft1.DraftNumber.Should().Be("DRF-PUR-0001");
            draft1.Version.Should().Be(1);

            var draft2 = await purchases.SaveDraftAsync(draft1.Id, draftPayload, session.UserId);
            draft2.Version.Should().Be(2);
            (await purchases.GetHeldDraftsAsync()).Should().ContainSingle(d => d.Id == draft1.Id);

            // Verify Bill Amt ±5.00 Guard rejects large mismatch
            draftPayload.DraftId = draft1.Id;
            draftPayload.InvoiceAmount = 212m; // Net is 200.00 -> diff 12.00 > 5.00
            var mismatchFail = await purchases.CreateAsync(draftPayload, session.UserId);
            mismatchFail.Succeeded.Should().BeFalse();
            mismatchFail.OffendingFieldName.Should().Be("invoice_amount");

            // Post Debit Purchase Invoice #1 (20 paid + 5 bonus = 25 units @ Rs. 200 subtotal -> line avg = 8.00)
            draftPayload.InvoiceAmount = 200m;
            var create1 = await purchases.CreateAsync(draftPayload, session.UserId);
            create1.Succeeded.Should().BeTrue(create1.Message);
            create1.PostedNumber.Should().Be("PRINV-0001");
            var inv1Id = create1.InvoiceId!.Value;

            // Held draft must now be marked converted (hidden from active held drafts)
            (await purchases.GetHeldDraftsAsync()).Should().BeEmpty();

            // Verify Product Moving Weighted Average Cost, Batch, PO Partial Receipt, Undelivered Demand & Cash Drawer
            await using (var checkDb1 = await dbFactory.CreateDbContextAsync())
            {
                var prod1 = await checkDb1.Products.AsNoTracking().SingleAsync(p => p.Id == prodId);
                prod1.Quantity.Should().Be(35); // 10 existing + 25 received (20 paid + 5 bonus)
                prod1.AvgPrice.Should().Be(8.57m); // (10*10 + 25*8) / 35 = 300 / 35 = 8.5714 -> 8.57

                var batch1 = await checkDb1.Batches.AsNoTracking().SingleAsync(b => b.ProductId == prodId && b.BatchNumber == "AUG-B1");
                batch1.Quantity.Should().Be(25);

                var poAfter1 = await checkDb1.PurchaseOrders.AsNoTracking().SingleAsync(p => p.Id == poId);
                poAfter1.Status.Should().Be("partially_received");

                var shortDemand = await checkDb1.UserDemands.AsNoTracking()
                    .SingleAsync(d => d.Notes == "Undelivered from Purchase Order PO-0001");
                shortDemand.Quantity.Should().Be(10); // 30 ordered - 20 received

                var drawerAfter1 = await checkDb1.CashDrawerSessions.AsNoTracking().SingleAsync(s => s.Status == "open");
                drawerAfter1.ExpectedClosingBalance.Should().Be(49800m); // 50,000 - 200 cash purchase
            }

            // Post Credit Purchase Invoice #2 receiving remaining 10 units on PO-0001 with Rs. 30 down-payment
            var creditReq = new PurchaseInvoiceUpsertRequest
            {
                PurchaseOrderId = poId,
                SupplierId = sup.Id,
                InvoiceType = "credit",
                PaymentMethod = "cash",
                PostedDate = DateTime.Today,
                InvoiceNumber = "SUP-BILL-102",
                InvoiceAmount = 100m,
                TotalAmount = 100m,
                TotalPaid = 30m,
                Items =
                [
                    new PurchaseInvoiceUpsertLineRequest
                    {
                        ProductId = prodId,
                        PackSize = 10,
                        Batch = "AUG-B2",
                        Expiry = DateTime.Today.AddMonths(18),
                        PackQuantity = 1m,
                        UnitQuantity = 10,
                        PackPurchasePrice = 100m,
                        UnitPurchasePrice = 10m,
                        PackSalePrice = 150m,
                        UnitSalePrice = 15m,
                        WholeSalePackPrice = 140m,
                        WholeSaleUnitPrice = 14m
                    }
                ]
            };

            var create2 = await purchases.CreateAsync(creditReq, session.UserId);
            create2.Succeeded.Should().BeTrue(create2.Message);
            create2.PostedNumber.Should().Be("PRINV-0002");
            var inv2Id = create2.InvoiceId!.Value;

            // Verify PO Full Receipt ("received"), Demand Cleared, and Supplier Credit Ledger Balance (100 - 30 = 70)
            var supCredit = await purchases.GetSupplierCreditSummaryAsync(sup.Id);
            supCredit.Should().NotBeNull();
            supCredit!.CurrentPayable.Should().Be(70m);

            await using (var checkDb2 = await dbFactory.CreateDbContextAsync())
            {
                var poAfter2 = await checkDb2.PurchaseOrders.AsNoTracking().SingleAsync(p => p.Id == poId);
                poAfter2.Status.Should().Be("received");

                var remainingShortDemands = await checkDb2.UserDemands.AsNoTracking()
                    .Where(d => d.Notes == "Undelivered from Purchase Order PO-0001")
                    .ToListAsync();
                remainingShortDemands.Should().BeEmpty();

                var ledgerRow = await checkDb2.SupplierLedgers.AsNoTracking()
                    .SingleAsync(l => l.PurchaseInvoiceId == inv2Id);
                ledgerRow.CreditRemaining.Should().Be(70m);
                ledgerRow.TotalBalance.Should().Be(70m);
            }

            // Update Credit Invoice #2 (increase PackPurchasePrice 100 -> 110 => Total 110, Paid 30 => Payable 80)
            creditReq.InvoiceAmount = 110m;
            creditReq.TotalAmount = 110m;
            creditReq.EditReason = "Supplier freight adjustment";
            creditReq.Items[0].PackPurchasePrice = 110m;
            creditReq.Items[0].UnitPurchasePrice = 11m;

            var update2 = await purchases.UpdateAsync(inv2Id, creditReq, session.UserId);
            update2.Succeeded.Should().BeTrue(update2.Message);

            var histories2 = await purchases.GetDocumentHistoryAsync(inv2Id);
            histories2.Select(h => h.Action).Should().Contain(["created", "updated"]);
            (await purchases.GetSupplierCreditSummaryAsync(sup.Id))!.CurrentPayable.Should().Be(80m);

            // Reverse Credit Invoice #2 -> PO re-opens to partially_received, Payable drops back to 0
            var rev2 = await purchases.ReverseAsync(inv2Id, "Damaged carton returned at gate", session.UserId);
            rev2.Succeeded.Should().BeTrue(rev2.Message);

            var detail2 = await purchases.GetByIdAsync(inv2Id);
            detail2!.Status.Should().Be("reversed");
            (await purchases.GetSupplierCreditSummaryAsync(sup.Id))!.CurrentPayable.Should().Be(0m);

            await using (var checkDb3 = await dbFactory.CreateDbContextAsync())
            {
                var poAfterRev = await checkDb3.PurchaseOrders.AsNoTracking().SingleAsync(p => p.Id == poId);
                poAfterRev.Status.Should().Be("partially_received");
            }

            // Delete Invoice #1 -> Wrong password rejected, correct password deletes & restores initial stock/cash
            var badDelete = await purchases.DeleteAsync(inv1Id, "wrong-pass", session.UserId);
            badDelete.Succeeded.Should().BeFalse();

            var okDelete = await purchases.DeleteAsync(inv1Id, "password", session.UserId);
            okDelete.Succeeded.Should().BeTrue(okDelete.Message);
            (await purchases.GetByIdAsync(inv1Id)).Should().BeNull();

            await using (var checkDb4 = await dbFactory.CreateDbContextAsync())
            {
                var prodFinal = await checkDb4.Products.AsNoTracking().SingleAsync(p => p.Id == prodId);
                prodFinal.Quantity.Should().Be(10); // Back to initial 10 units

                var poFinal = await checkDb4.PurchaseOrders.AsNoTracking().SingleAsync(p => p.Id == poId);
                poFinal.Status.Should().Be("ordered"); // All receipts reversed/deleted -> back to ordered

                var drawerFinal = await checkDb4.CashDrawerSessions.AsNoTracking().SingleAsync(s => s.Status == "open");
                drawerFinal.ExpectedClosingBalance.Should().Be(50000m); // All cash refunded
            }

            // Verify ProductSearchInput.jsx / ProductFormModal.jsx Modal Window Size & Placement Persistence per User
            var defaultLayout = await purchases.GetModalWindowLayoutAsync("productSearchModal", session.UserId);
            defaultLayout.Width.Should().Be(960d);
            defaultLayout.Height.Should().Be(600d);
            defaultLayout.HasSavedPosition.Should().BeFalse();

            await purchases.SaveModalWindowLayoutAsync(
                "productSearchModal",
                new PurchaseModalWindowLayoutDto { X = 148.5, Y = 84.0, Width = 1080.0, Height = 680.0 },
                session.UserId);

            // Also test MIN_WIDTH (640) & MIN_HEIGHT (420) clamping on save/read
            var savedSearchLayout = await purchases.GetModalWindowLayoutAsync("productSearchModal", session.UserId);
            savedSearchLayout.HasSavedPosition.Should().BeTrue();
            savedSearchLayout.X.Should().Be(148.5);
            savedSearchLayout.Y.Should().Be(84.0);
            savedSearchLayout.Width.Should().Be(1080.0);
            savedSearchLayout.Height.Should().Be(680.0);

            var bootAfterLayoutSave = await purchases.GetFormBootstrapAsync();
            bootAfterLayoutSave.ProductSearchModalLayout.Width.Should().Be(1080.0);
            bootAfterLayoutSave.ProductSearchModalLayout.Height.Should().Be(680.0);
            bootAfterLayoutSave.ProductSearchModalLayout.X.Should().Be(148.5);
            bootAfterLayoutSave.ProductSearchModalLayout.Y.Should().Be(84.0);
        }
        finally
        {
            if (Directory.Exists(tempDir)) try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task SaleInvoiceService_RetailAndWholesale_ShouldSupportFefoBatches_CustomerLedger_CustomWholesalePricing_AndReversal()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "karobar_sale_inv_test_" + Guid.NewGuid().ToString("N"));
        try
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddKarobarInfrastructure(tempDir);

            await using var provider = services.BuildServiceProvider();
            var dbInit = provider.GetRequiredService<IDatabaseInitializerService>();
            var auth = provider.GetRequiredService<IAuthService>();
            var categories = provider.GetRequiredService<ICategoryService>();
            var brands = provider.GetRequiredService<IBrandService>();
            var suppliers = provider.GetRequiredService<ISupplierService>();
            var products = provider.GetRequiredService<IProductService>();
            var sales = provider.GetRequiredService<ISaleInvoiceService>();
            var dbFactory = provider.GetRequiredService<IDbContextFactory<KarobarDbContext>>();

            await dbInit.InitializeAndSeedAsync();
            var session = await auth.LoginAsync("admin", "password");

            var cat = await categories.CreateAsync("Analgesics");
            var br = await brands.CreateAsync(new BrandUpsertRequest { Name = "Haleon" });
            var sup = await suppliers.CreateAsync(new SupplierUpsertRequest
            {
                Name = "Haleon Distributors",
                Phone = "0300-1112233",
                CreditDays = 30
            });

            // 1. Create Product with PackSize = 10, Quantity = 50 (30 for B-LATE + 20 for B-EARLY), Cost = 10/unit, Retail = 20/unit, Wholesale = 16/unit
            var prodCreate = await products.CreateProductAsync(new ProductUpsertRequest
            {
                ProductCode = "PRD-PAN500",
                Barcode = "896400099911",
                Name = "Panadol Extra 500mg",
                Formulation = "Paracetamol + Caffeine",
                PackSize = 10,
                Quantity = 50,
                PackPurchasePrice = 100m,
                UnitPurchasePrice = 10m,
                PackSalePrice = 200m,
                UnitSalePrice = 20m,
                WholeSalePackPrice = 160m,
                WholeSaleUnitPrice = 16m,
                AvgPrice = 10m,
                MaxDiscount = 15m,
                CategoryId = cat.Id,
                BrandId = br.Id,
                SupplierId = sup.Id
            });
            prodCreate.Succeeded.Should().BeTrue(prodCreate.Message);
            var prodId = prodCreate.EntityId!.Value;

            // Create two batches: B-LATE (expiring in 18 months, 30 units) and B-EARLY (expiring in 6 months, 20 units)
            var bLate = await products.SaveProductBatchAsync(prodId, new BatchSaveRequest
            {
                BatchNumber = "B-LATE",
                ExpiryDate = DateTime.Today.AddMonths(18),
                Quantity = 30,
                UnitPurchasePrice = 10m,
                UnitSalePrice = 20m
            });
            bLate.Succeeded.Should().BeTrue(bLate.Message);

            var bEarly = await products.SaveProductBatchAsync(prodId, new BatchSaveRequest
            {
                BatchNumber = "B-EARLY",
                ExpiryDate = DateTime.Today.AddMonths(6),
                Quantity = 20,
                UnitPurchasePrice = 10m,
                UnitSalePrice = 20m
            });
            bEarly.Succeeded.Should().BeTrue(bEarly.Message);

            // 2. Verify FEFO Batch Sorting & Prefix Product Search
            var searchResults = await sales.SearchProductsForSaleAsync("Panadol");
            searchResults.Should().ContainSingle(p => p.Id == prodId);
            searchResults[0].Batches.First().BatchNumber.Should().Be("B-EARLY");

            // 3. Open Cash Drawer Shift & Create Retail Cash (Debit) Sale for 5 units from B-EARLY @ Rs. 20 = Rs. 100
            await sales.OpenCashDrawerSessionAsync(10000m, "Test Shift", session.UserId);
            var boot = await sales.GetFormBootstrapAsync();
            boot.HasOpenCashDrawer.Should().BeTrue();

            var retailRes = await sales.CreateAsync(new SaleInvoiceUpsertRequest
            {
                Date = DateTime.Today,
                CustomerId = boot.WalkInCustomerId,
                SaleType = "retail",
                WholesaleType = "unit",
                InvoiceType = "debit",
                GrossAmount = 100m,
                Total = 100m,
                TotalReceive = 100m,
                CashGiven = 500m,
                PaymentMethod = "cash",
                Items =
                [
                    new SaleInvoiceLineItemDto
                    {
                        ProductId = prodId,
                        BatchId = bEarly.EntityId,
                        BatchNumber = "B-EARLY",
                        PackSize = 10,
                        Quantity = 5,
                        UnitQuantity = 5,
                        Price = 20m,
                        PackPrice = 200m,
                        AvgPrice = 10m,
                        UnitPurchasePrice = 10m,
                        SubTotal = 100m
                    }
                ]
            }, session.UserId);
            retailRes.Succeeded.Should().BeTrue(retailRes.Message);
            retailRes.PostedNumber.Should().Be("SI-000001");

            // Verify B-EARLY stock decreased from 20 -> 15 and Total Product stock decreased from 50 -> 45
            var batchesAfterRetail = await sales.GetProductBatchesForSaleAsync(prodId);
            batchesAfterRetail.Single(b => b.BatchNumber == "B-EARLY").Quantity.Should().Be(15);

            // 4. Create B2B Customer & Wholesale Credit Sale with Custom Wholesale Pack Price (175/pack = 17.50/unit) + Bonus
            var wsCust = await sales.QuickCreateCustomerAsync("Al-Shifa Medical Store", "0321-7778899", "wholesale");
            var wsRes = await sales.CreateAsync(new SaleInvoiceUpsertRequest
            {
                Date = DateTime.Today,
                CustomerId = wsCust.Id,
                SaleType = "wholesale",
                WholesaleType = "pack",
                InvoiceType = "credit",
                TotalReceive = 75m, // Partial cash received now out of 175 => Remaining Credit Due = 100
                PaymentMethod = "cash",
                Items =
                [
                    new SaleInvoiceLineItemDto
                    {
                        ProductId = prodId,
                        BatchId = bLate.EntityId,
                        BatchNumber = "B-LATE",
                        PackSize = 10,
                        PackQuantity = 1m,
                        UnitQuantity = 10,
                        Quantity = 10,
                        PackBonus = 0.2m,
                        UnitBonus = 2,
                        Bonus = 2,
                        PackPrice = 175m,
                        Price = 17.5m,
                        AvgPrice = 10m,
                        UnitPurchasePrice = 10m,
                        IsCustomPrice = true,
                        SubTotal = 175m
                    }
                ]
            }, session.UserId);
            wsRes.Succeeded.Should().BeTrue(wsRes.Message);
            wsRes.PostedNumber.Should().Be("SI-000002");

            // Verify B-LATE stock deducted by Quantity (10) + Bonus (2) = 12 units (30 -> 18)
            var batchesAfterWs = await sales.GetProductBatchesForSaleAsync(prodId);
            batchesAfterWs.Single(b => b.BatchNumber == "B-LATE").Quantity.Should().Be(18);

            // Verify Custom Wholesale Price persisted for Al-Shifa Medical Store and Customer Ledger Balance = 100
            var wsDetail = await sales.GetByIdAsync(wsRes.InvoiceId!.Value);
            wsDetail.Should().NotBeNull();
            wsDetail!.RemainThisBill.Should().Be(100m);
            wsDetail.TotalBalance.Should().Be(100m);

            // Verify Show-page adjacent navigation & Record Payment against remaining credit balance
            var adjId = await sales.GetAdjacentInvoiceIdAsync(wsRes.InvoiceId.Value);
            adjId.Should().Be(retailRes.InvoiceId);

            var payRes = await sales.RecordInvoicePaymentAsync(
                wsRes.InvoiceId.Value,
                40m,
                boot.Accounts.FirstOrDefault()?.Id,
                "CHQ-9988",
                session.UserId);
            payRes.Succeeded.Should().BeTrue(payRes.Message);

            var wsDetailAfterPay = await sales.GetByIdAsync(wsRes.InvoiceId.Value);
            wsDetailAfterPay!.TotalReceive.Should().Be(115m);
            wsDetailAfterPay.RemainThisBill.Should().Be(60m);
            wsDetailAfterPay.TotalBalance.Should().Be(60m);

            var searchForWsCustomer = await sales.SearchProductsForSaleAsync("Panadol", wsCust.Id);
            searchForWsCustomer[0].WholeSalePackPrice.Should().Be(175m);
            searchForWsCustomer[0].IsCustomCustomerPrice.Should().BeTrue();

            // 5. Verify 0% Wholesale Margin Floor Block (selling below cost = Rs. 8/unit when cost is Rs. 10/unit)
            var negMarginRes = await sales.CreateAsync(new SaleInvoiceUpsertRequest
            {
                Date = DateTime.Today,
                CustomerId = wsCust.Id,
                SaleType = "wholesale",
                InvoiceType = "debit",
                Items =
                [
                    new SaleInvoiceLineItemDto
                    {
                        ProductId = prodId,
                        BatchId = bLate.EntityId,
                        PackSize = 10,
                        PackQuantity = 1m,
                        UnitQuantity = 10,
                        Quantity = 10,
                        PackPrice = 80m,
                        Price = 8m,
                        AvgPrice = 10m,
                        UnitPurchasePrice = 10m
                    }
                ]
            }, session.UserId);
            negMarginRes.Succeeded.Should().BeFalse();
            negMarginRes.Message.Should().Contain("0%");

            // 6. Reverse Wholesale Invoice -> Restores 12 units to B-LATE (18 -> 30) and clears Customer Ledger
            var revRes = await sales.ReverseAsync(wsRes.InvoiceId.Value, "Order cancelled by pharmacy", session.UserId);
            revRes.Succeeded.Should().BeTrue(revRes.Message);

            var batchesAfterRev = await sales.GetProductBatchesForSaleAsync(prodId);
            batchesAfterRev.Single(b => b.BatchNumber == "B-LATE").Quantity.Should().Be(30);
        }
        finally
        {
            if (Directory.Exists(tempDir)) try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }
}
