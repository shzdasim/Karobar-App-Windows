using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Karobar.Domain.Entities;

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
