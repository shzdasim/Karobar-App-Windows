using System.IO.Compression;
using System.Text;
using FluentAssertions;
using Karobar.Application.Interfaces;
using Karobar.Domain.Constants;
using Karobar.Domain.Formulas;
using Karobar.Infrastructure;
using Karobar.Infrastructure.Importing;
using Karobar.Infrastructure.Security;
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
}
