using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Karobar.Domain.Entities;

[Table("customer_ledgers")]
public class CustomerLedger
{
    [Key]
    [Column("id")]
    public long Id { get; set; }

    [Column("customer_id")]
    public long CustomerId { get; set; }

    [Column("sale_invoice_id")]
    public long? SaleInvoiceId { get; set; }

    [Column("sale_return_id")]
    public long? SaleReturnId { get; set; }

    [Column("payment_id")]
    public long? PaymentId { get; set; }

    [Column("entry_type")]
    public string EntryType { get; set; } = "invoice";

    [Column("posted_number")]
    public string? PostedNumber { get; set; }

    [Column("invoice_number")]
    public string? InvoiceNumber { get; set; }

    [Column("entry_date")]
    public DateTime EntryDate { get; set; }

    [Column("due_date")]
    public DateTime? DueDate { get; set; }

    [Column("description")]
    public string? Description { get; set; }

    [Column("payment_method")]
    public string? PaymentMethod { get; set; }

    [Column("payment_reference")]
    public string? PaymentReference { get; set; }

    [Column("invoice_total")]
    public decimal InvoiceTotal { get; set; }

    [Column("total_received")]
    public decimal TotalReceived { get; set; }

    [Column("deducted_amount")]
    public decimal DeductedAmount { get; set; }

    [Column("credited_amount")]
    public decimal CreditedAmount { get; set; }

    [Column("balance_remaining")]
    public decimal BalanceRemaining { get; set; }

    [Column("previous_balance")]
    public decimal PreviousBalance { get; set; }

    [Column("total_balance")]
    public decimal TotalBalance { get; set; }

    [Column("created_by")]
    public long? CreatedBy { get; set; }

    [Column("created_at")]
    public DateTime? CreatedAt { get; set; }

    [Column("updated_at")]
    public DateTime? UpdatedAt { get; set; }
}

[Table("supplier_ledgers")]
public class SupplierLedger
{
    [Key]
    [Column("id")]
    public long Id { get; set; }

    [Column("supplier_id")]
    public long SupplierId { get; set; }

    [Column("purchase_invoice_id")]
    public long? PurchaseInvoiceId { get; set; }

    [Column("purchase_return_id")]
    public long? PurchaseReturnId { get; set; }

    [Column("payment_id")]
    public long? PaymentId { get; set; }

    [Column("entry_type")]
    public string EntryType { get; set; } = "invoice";

    [Column("posted_number")]
    public string? PostedNumber { get; set; }

    [Column("invoice_number")]
    public string? InvoiceNumber { get; set; }

    [Column("entry_date")]
    public DateTime EntryDate { get; set; }

    [Column("due_date")]
    public DateTime? DueDate { get; set; }

    [Column("description")]
    public string? Description { get; set; }

    [Column("payment_reference")]
    public string? PaymentReference { get; set; }

    [Column("invoice_total")]
    public decimal InvoiceTotal { get; set; }

    [Column("total_paid")]
    public decimal TotalPaid { get; set; }

    [Column("debited_amount")]
    public decimal DebitedAmount { get; set; }

    [Column("credit_remaining")]
    public decimal CreditRemaining { get; set; }

    [Column("previous_balance")]
    public decimal PreviousBalance { get; set; }

    [Column("total_balance")]
    public decimal TotalBalance { get; set; }

    [Column("created_by")]
    public long? CreatedBy { get; set; }

    [Column("created_at")]
    public DateTime? CreatedAt { get; set; }

    [Column("updated_at")]
    public DateTime? UpdatedAt { get; set; }
}

[Table("payments")]
public class Payment
{
    [Key]
    [Column("id")]
    public long Id { get; set; }

    [Column("voucher_number")]
    public string VoucherNumber { get; set; } = string.Empty;

    [Column("party_type")]
    public string PartyType { get; set; } = "customer";

    [Column("customer_id")]
    public long? CustomerId { get; set; }

    [Column("supplier_id")]
    public long? SupplierId { get; set; }

    [Column("payment_date")]
    public DateTime PaymentDate { get; set; }

    [Column("amount")]
    public decimal Amount { get; set; }

    [Column("allocated_amount")]
    public decimal AllocatedAmount { get; set; }

    [Column("unapplied_amount")]
    public decimal UnappliedAmount { get; set; }

    [Column("payment_method")]
    public string PaymentMethod { get; set; } = "cash";

    [Column("account_id")]
    public long? AccountId { get; set; }

    [Column("reference_number")]
    public string? ReferenceNumber { get; set; }

    [Column("cheque_number")]
    public string? ChequeNumber { get; set; }

    [Column("cheque_date")]
    public DateTime? ChequeDate { get; set; }

    [Column("bank_name")]
    public string? BankName { get; set; }

    [Column("cheque_status")]
    public string? ChequeStatus { get; set; }

    [Column("cheque_cleared_at")]
    public DateTime? ChequeClearedAt { get; set; }

    [Column("cheque_bounced_at")]
    public DateTime? ChequeBouncedAt { get; set; }

    [Column("cheque_bounce_reason")]
    public string? ChequeBounceReason { get; set; }

    [Column("status")]
    public string Status { get; set; } = "posted";

    [Column("reversed_at")]
    public DateTime? ReversedAt { get; set; }

    [Column("reversed_by")]
    public long? ReversedBy { get; set; }

    [Column("reversal_reason")]
    public string? ReversalReason { get; set; }

    [Column("notes")]
    public string? Notes { get; set; }

    [Column("created_by")]
    public long? CreatedBy { get; set; }

    [Column("created_at")]
    public DateTime? CreatedAt { get; set; }

    [Column("updated_at")]
    public DateTime? UpdatedAt { get; set; }
}

[Table("payment_allocations")]
public class PaymentAllocation
{
    [Key]
    [Column("id")]
    public long Id { get; set; }

    [Column("payment_id")]
    public long PaymentId { get; set; }

    [Column("invoice_type")]
    public string InvoiceType { get; set; } = "sale_invoice";

    [Column("invoice_id")]
    public long InvoiceId { get; set; }

    [Column("allocated_amount")]
    public decimal AllocatedAmount { get; set; }

    [Column("created_at")]
    public DateTime? CreatedAt { get; set; }

    [Column("updated_at")]
    public DateTime? UpdatedAt { get; set; }
}

[Table("accounts")]
public class Account
{
    [Key]
    [Column("id")]
    public long Id { get; set; }

    [Column("name")]
    public string Name { get; set; } = string.Empty;

    [Column("code")]
    public string Code { get; set; } = string.Empty;

    [Column("account_type")]
    public string AccountType { get; set; } = "bank";

    [Column("provider_key")]
    public string? ProviderKey { get; set; }

    [Column("account_title")]
    public string? AccountTitle { get; set; }

    [Column("account_number")]
    public string? AccountNumber { get; set; }

    [Column("iban")]
    public string? Iban { get; set; }

    [Column("branch_name")]
    public string? BranchName { get; set; }

    [Column("branch_code")]
    public string? BranchCode { get; set; }

    [Column("opening_balance")]
    public decimal OpeningBalance { get; set; }

    [Column("current_balance")]
    public decimal CurrentBalance { get; set; }

    [Column("currency")]
    public string Currency { get; set; } = "PKR";

    [Column("is_default")]
    public bool IsDefault { get; set; }

    [Column("is_active")]
    public bool IsActive { get; set; } = true;

    [Column("sync_with_cash_drawer")]
    public bool SyncWithCashDrawer { get; set; }

    [Column("notes")]
    public string? Notes { get; set; }

    [Column("created_by")]
    public long? CreatedBy { get; set; }

    [Column("created_at")]
    public DateTime? CreatedAt { get; set; }

    [Column("updated_at")]
    public DateTime? UpdatedAt { get; set; }
}

[Table("account_transactions")]
public class AccountTransaction
{
    [Key]
    [Column("id")]
    public long Id { get; set; }

    [Column("account_id")]
    public long AccountId { get; set; }

    [Column("transaction_date")]
    public DateTime TransactionDate { get; set; }

    [Column("direction")]
    public string Direction { get; set; } = "in";

    [Column("amount")]
    public decimal Amount { get; set; }

    [Column("balance_after")]
    public decimal BalanceAfter { get; set; }

    [Column("category")]
    public string Category { get; set; } = "general";

    [Column("payment_method")]
    public string? PaymentMethod { get; set; }

    [Column("reference_type")]
    public string? ReferenceType { get; set; }

    [Column("reference_id")]
    public long? ReferenceId { get; set; }

    [Column("reference_number")]
    public string? ReferenceNumber { get; set; }

    [Column("counterparty_name")]
    public string? CounterpartyName { get; set; }

    [Column("transfer_group_id")]
    public string? TransferGroupId { get; set; }

    [Column("description")]
    public string? Description { get; set; }

    [Column("created_by")]
    public long? CreatedBy { get; set; }

    [Column("created_at")]
    public DateTime? CreatedAt { get; set; }

    [Column("updated_at")]
    public DateTime? UpdatedAt { get; set; }
}

[Table("cash_drawer_sessions")]
public class CashDrawerSession
{
    [Key]
    [Column("id")]
    public long Id { get; set; }

    [Column("opened_by")]
    public long? OpenedBy { get; set; }

    [Column("closed_by")]
    public long? ClosedBy { get; set; }

    [Column("opened_at")]
    public DateTime OpenedAt { get; set; }

    [Column("closed_at")]
    public DateTime? ClosedAt { get; set; }

    [Column("opening_balance")]
    public decimal OpeningBalance { get; set; }

    [Column("expected_closing_balance")]
    public decimal ExpectedClosingBalance { get; set; }

    [Column("actual_closing_balance")]
    public decimal? ActualClosingBalance { get; set; }

    [Column("difference")]
    public decimal? Difference { get; set; }

    [Column("status")]
    public string Status { get; set; } = "open";

    [Column("opening_note")]
    public string? OpeningNote { get; set; }

    [Column("closing_note")]
    public string? ClosingNote { get; set; }

    [Column("created_at")]
    public DateTime? CreatedAt { get; set; }

    [Column("updated_at")]
    public DateTime? UpdatedAt { get; set; }
}

[Table("cash_drawer_transactions")]
public class CashDrawerTransaction
{
    [Key]
    [Column("id")]
    public long Id { get; set; }

    [Column("cash_drawer_session_id")]
    public long CashDrawerSessionId { get; set; }

    [Column("type")]
    public string Type { get; set; } = "cash_in";

    [Column("amount")]
    public decimal Amount { get; set; }

    [Column("reference_type")]
    public string? ReferenceType { get; set; }

    [Column("reference_id")]
    public long? ReferenceId { get; set; }

    [Column("description")]
    public string? Description { get; set; }

    [Column("created_by")]
    public long? CreatedBy { get; set; }

    [Column("created_at")]
    public DateTime? CreatedAt { get; set; }

    [Column("updated_at")]
    public DateTime? UpdatedAt { get; set; }
}

[Table("expense_categories")]
public class ExpenseCategory
{
    [Key]
    [Column("id")]
    public long Id { get; set; }

    [Column("name")]
    public string Name { get; set; } = string.Empty;

    [Column("slug")]
    public string Slug { get; set; } = string.Empty;

    [Column("description")]
    public string? Description { get; set; }

    [Column("color")]
    public string Color { get; set; } = "#6366f1";

    [Column("is_system")]
    public bool IsSystem { get; set; }

    [Column("is_active")]
    public bool IsActive { get; set; } = true;

    [Column("created_at")]
    public DateTime? CreatedAt { get; set; }

    [Column("updated_at")]
    public DateTime? UpdatedAt { get; set; }
}

[Table("expenses")]
public class Expense
{
    [Key]
    [Column("id")]
    public long Id { get; set; }

    [Column("expense_number")]
    public string ExpenseNumber { get; set; } = string.Empty;

    [Column("expense_category_id")]
    public long ExpenseCategoryId { get; set; }

    [Column("title")]
    public string Title { get; set; } = string.Empty;

    [Column("vendor_name")]
    public string? VendorName { get; set; }

    [Column("expense_date")]
    public DateTime ExpenseDate { get; set; }

    [Column("amount")]
    public decimal Amount { get; set; }

    [Column("payment_method")]
    public string PaymentMethod { get; set; } = "cash";

    [Column("account_id")]
    public long? AccountId { get; set; }

    [Column("deduct_from_cash_drawer")]
    public bool DeductFromCashDrawer { get; set; }

    [Column("cash_drawer_transaction_id")]
    public long? CashDrawerTransactionId { get; set; }

    [Column("reference_number")]
    public string? ReferenceNumber { get; set; }

    [Column("receipt_path")]
    public string? ReceiptPath { get; set; }

    [Column("notes")]
    public string? Notes { get; set; }

    [Column("is_recurring")]
    public bool IsRecurring { get; set; }

    [Column("recurring_frequency")]
    public string? RecurringFrequency { get; set; }

    [Column("next_due_date")]
    public DateTime? NextDueDate { get; set; }

    [Column("parent_expense_id")]
    public long? ParentExpenseId { get; set; }

    [Column("created_by")]
    public long? CreatedBy { get; set; }

    [Column("created_at")]
    public DateTime? CreatedAt { get; set; }

    [Column("updated_at")]
    public DateTime? UpdatedAt { get; set; }
}

[Table("chart_of_accounts")]
public class ChartOfAccount
{
    [Key]
    [Column("id")]
    public long Id { get; set; }

    [Column("code")]
    public string Code { get; set; } = string.Empty;

    [Column("name")]
    public string Name { get; set; } = string.Empty;

    [Column("account_type")]
    public string AccountType { get; set; } = "asset";

    [Column("sub_type")]
    public string? SubType { get; set; }

    [Column("normal_balance")]
    public string NormalBalance { get; set; } = "debit";

    [Column("opening_balance")]
    public decimal OpeningBalance { get; set; }

    [Column("is_system")]
    public bool IsSystem { get; set; }

    [Column("is_active")]
    public bool IsActive { get; set; } = true;

    [Column("description")]
    public string? Description { get; set; }

    [Column("created_at")]
    public DateTime? CreatedAt { get; set; }

    [Column("updated_at")]
    public DateTime? UpdatedAt { get; set; }
}

[Table("journal_entries")]
public class JournalEntry
{
    [Key]
    [Column("id")]
    public long Id { get; set; }

    [Column("entry_number")]
    public string EntryNumber { get; set; } = string.Empty;

    [Column("entry_date")]
    public DateTime EntryDate { get; set; }

    [Column("reference_number")]
    public string? ReferenceNumber { get; set; }

    [Column("source_type")]
    public string? SourceType { get; set; }

    [Column("source_id")]
    public long? SourceId { get; set; }

    [Column("description")]
    public string Description { get; set; } = string.Empty;

    [Column("status")]
    public string Status { get; set; } = "posted";

    [Column("is_system_generated")]
    public bool IsSystemGenerated { get; set; }

    [Column("total_debit")]
    public decimal TotalDebit { get; set; }

    [Column("total_credit")]
    public decimal TotalCredit { get; set; }

    [Column("created_by")]
    public long? CreatedBy { get; set; }

    [Column("reversed_by")]
    public long? ReversedBy { get; set; }

    [Column("reversed_at")]
    public DateTime? ReversedAt { get; set; }

    [Column("created_at")]
    public DateTime? CreatedAt { get; set; }

    [Column("updated_at")]
    public DateTime? UpdatedAt { get; set; }
}

[Table("journal_entry_lines")]
public class JournalEntryLine
{
    [Key]
    [Column("id")]
    public long Id { get; set; }

    [Column("journal_entry_id")]
    public long JournalEntryId { get; set; }

    [Column("chart_of_account_id")]
    public long ChartOfAccountId { get; set; }

    [Column("party_type")]
    public string? PartyType { get; set; }

    [Column("party_id")]
    public long? PartyId { get; set; }

    [Column("party_name")]
    public string? PartyName { get; set; }

    [Column("debit")]
    public decimal Debit { get; set; }

    [Column("credit")]
    public decimal Credit { get; set; }

    [Column("memo")]
    public string? Memo { get; set; }

    [Column("created_at")]
    public DateTime? CreatedAt { get; set; }

    [Column("updated_at")]
    public DateTime? UpdatedAt { get; set; }
}

[Table("tax_rates")]
public class TaxRate
{
    [Key]
    [Column("id")]
    public long Id { get; set; }

    [Column("name")]
    public string Name { get; set; } = string.Empty;

    [Column("code")]
    public string Code { get; set; } = string.Empty;

    [Column("rate")]
    public decimal Rate { get; set; }

    [Column("tax_type")]
    public string TaxType { get; set; } = "gst";

    [Column("applies_to")]
    public string AppliesTo { get; set; } = "both";

    [Column("is_inclusive")]
    public bool IsInclusive { get; set; }

    [Column("is_default")]
    public bool IsDefault { get; set; }

    [Column("is_active")]
    public bool IsActive { get; set; } = true;

    [Column("description")]
    public string? Description { get; set; }

    [Column("created_at")]
    public DateTime? CreatedAt { get; set; }

    [Column("updated_at")]
    public DateTime? UpdatedAt { get; set; }
}

[Table("employees")]
public class Employee
{
    [Key]
    [Column("id")]
    public long Id { get; set; }

    [Column("employee_code")]
    public string EmployeeCode { get; set; } = string.Empty;

    [Column("user_id")]
    public long? UserId { get; set; }

    [Column("name")]
    public string Name { get; set; } = string.Empty;

    [Column("father_name")]
    public string? FatherName { get; set; }

    [Column("cnic")]
    public string? Cnic { get; set; }

    [Column("phone")]
    public string? Phone { get; set; }

    [Column("email")]
    public string? Email { get; set; }

    [Column("address")]
    public string? Address { get; set; }

    [Column("designation")]
    public string Designation { get; set; } = "Salesman";

    [Column("department")]
    public string? Department { get; set; } = "Sales";

    [Column("employment_type")]
    public string EmploymentType { get; set; } = "full_time";

    [Column("status")]
    public string Status { get; set; } = "active";

    [Column("join_date")]
    public DateTime? JoinDate { get; set; }

    [Column("leave_date")]
    public DateTime? LeaveDate { get; set; }

    [Column("shift_start")]
    public string? ShiftStart { get; set; } = "09:00";

    [Column("shift_end")]
    public string? ShiftEnd { get; set; } = "21:00";

    [Column("working_days_per_month")]
    public int WorkingDaysPerMonth { get; set; } = 26;

    [Column("base_salary")]
    public decimal BaseSalary { get; set; }

    [Column("commission_type")]
    public string CommissionType { get; set; } = "none";

    [Column("commission_rate")]
    public decimal CommissionRate { get; set; }

    [Column("sales_target")]
    public decimal SalesTarget { get; set; }

    [Column("bank_name")]
    public string? BankName { get; set; }

    [Column("bank_account_number")]
    public string? BankAccountNumber { get; set; }

    [Column("notes")]
    public string? Notes { get; set; }

    [Column("created_at")]
    public DateTime? CreatedAt { get; set; }

    [Column("updated_at")]
    public DateTime? UpdatedAt { get; set; }
}

[Table("employee_attendances")]
public class EmployeeAttendance
{
    [Key]
    [Column("id")]
    public long Id { get; set; }

    [Column("employee_id")]
    public long EmployeeId { get; set; }

    [Column("attendance_date")]
    public DateTime AttendanceDate { get; set; }

    [Column("status")]
    public string Status { get; set; } = "present";

    [Column("check_in")]
    public string? CheckIn { get; set; }

    [Column("check_out")]
    public string? CheckOut { get; set; }

    [Column("hours_worked")]
    public decimal HoursWorked { get; set; }

    [Column("overtime_hours")]
    public decimal OvertimeHours { get; set; }

    [Column("late_minutes")]
    public int LateMinutes { get; set; }

    [Column("notes")]
    public string? Notes { get; set; }

    [Column("recorded_by")]
    public long? RecordedBy { get; set; }

    [Column("created_at")]
    public DateTime? CreatedAt { get; set; }

    [Column("updated_at")]
    public DateTime? UpdatedAt { get; set; }
}

[Table("employee_advances")]
public class EmployeeAdvance
{
    [Key]
    [Column("id")]
    public long Id { get; set; }

    [Column("advance_number")]
    public string AdvanceNumber { get; set; } = string.Empty;

    [Column("employee_id")]
    public long EmployeeId { get; set; }

    [Column("advance_date")]
    public DateTime AdvanceDate { get; set; }

    [Column("amount")]
    public decimal Amount { get; set; }

    [Column("recovered_amount")]
    public decimal RecoveredAmount { get; set; }

    [Column("remaining_amount")]
    public decimal RemainingAmount { get; set; }

    [Column("payment_method")]
    public string PaymentMethod { get; set; } = "cash";

    [Column("account_id")]
    public long? AccountId { get; set; }

    [Column("deduct_from_cash_drawer")]
    public bool DeductFromCashDrawer { get; set; }

    [Column("status")]
    public string Status { get; set; } = "disbursed";

    [Column("reason")]
    public string? Reason { get; set; }

    [Column("created_by")]
    public long? CreatedBy { get; set; }

    [Column("created_at")]
    public DateTime? CreatedAt { get; set; }

    [Column("updated_at")]
    public DateTime? UpdatedAt { get; set; }
}

[Table("payroll_runs")]
public class PayrollRun
{
    [Key]
    [Column("id")]
    public long Id { get; set; }

    [Column("run_number")]
    public string RunNumber { get; set; } = string.Empty;

    [Column("period_month")]
    public string PeriodMonth { get; set; } = string.Empty;

    [Column("period_start")]
    public DateTime PeriodStart { get; set; }

    [Column("period_end")]
    public DateTime PeriodEnd { get; set; }

    [Column("status")]
    public string Status { get; set; } = "draft";

    [Column("employee_count")]
    public int EmployeeCount { get; set; }

    [Column("total_base_salary")]
    public decimal TotalBaseSalary { get; set; }

    [Column("total_commissions")]
    public decimal TotalCommissions { get; set; }

    [Column("total_bonuses")]
    public decimal TotalBonuses { get; set; }

    [Column("total_deductions")]
    public decimal TotalDeductions { get; set; }

    [Column("total_advance_recovery")]
    public decimal TotalAdvanceRecovery { get; set; }

    [Column("total_net_payable")]
    public decimal TotalNetPayable { get; set; }

    [Column("total_paid")]
    public decimal TotalPaid { get; set; }

    [Column("notes")]
    public string? Notes { get; set; }

    [Column("created_by")]
    public long? CreatedBy { get; set; }

    [Column("approved_by")]
    public long? ApprovedBy { get; set; }

    [Column("approved_at")]
    public DateTime? ApprovedAt { get; set; }

    [Column("created_at")]
    public DateTime? CreatedAt { get; set; }

    [Column("updated_at")]
    public DateTime? UpdatedAt { get; set; }
}

[Table("payroll_items")]
public class PayrollItem
{
    [Key]
    [Column("id")]
    public long Id { get; set; }

    [Column("payroll_run_id")]
    public long PayrollRunId { get; set; }

    [Column("employee_id")]
    public long EmployeeId { get; set; }

    [Column("working_days")]
    public int WorkingDays { get; set; } = 26;

    [Column("present_days")]
    public decimal PresentDays { get; set; }

    [Column("absent_days")]
    public decimal AbsentDays { get; set; }

    [Column("paid_leave_days")]
    public decimal PaidLeaveDays { get; set; }

    [Column("overtime_hours")]
    public decimal OvertimeHours { get; set; }

    [Column("base_salary")]
    public decimal BaseSalary { get; set; }

    [Column("earned_salary")]
    public decimal EarnedSalary { get; set; }

    [Column("overtime_pay")]
    public decimal OvertimePay { get; set; }

    [Column("sales_count")]
    public int SalesCount { get; set; }

    [Column("sales_amount")]
    public decimal SalesAmount { get; set; }

    [Column("gross_profit_amount")]
    public decimal GrossProfitAmount { get; set; }

    [Column("commission_amount")]
    public decimal CommissionAmount { get; set; }

    [Column("bonus_amount")]
    public decimal BonusAmount { get; set; }

    [Column("absence_deduction")]
    public decimal AbsenceDeduction { get; set; }

    [Column("other_deduction")]
    public decimal OtherDeduction { get; set; }

    [Column("advance_recovery")]
    public decimal AdvanceRecovery { get; set; }

    [Column("net_payable")]
    public decimal NetPayable { get; set; }

    [Column("paid_amount")]
    public decimal PaidAmount { get; set; }

    [Column("payment_status")]
    public string PaymentStatus { get; set; } = "unpaid";

    [Column("payment_method")]
    public string? PaymentMethod { get; set; }

    [Column("account_id")]
    public long? AccountId { get; set; }

    [Column("expense_id")]
    public long? ExpenseId { get; set; }

    [Column("paid_at")]
    public DateTime? PaidAt { get; set; }

    [Column("notes")]
    public string? Notes { get; set; }

    [Column("created_at")]
    public DateTime? CreatedAt { get; set; }

    [Column("updated_at")]
    public DateTime? UpdatedAt { get; set; }
}
