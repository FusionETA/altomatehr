using System.ComponentModel.DataAnnotations;

namespace AltomateHR.Api.Modules.Payroll.Dtos;

// Hand-entered CP8D rows, for the converter on the Annual forms page.
//
// Everything here is typed in by the admin rather than read from payroll,
// because the whole point is the years this system did not run: a mid-year
// cutover, a one-off correction, or a dry run against LHDN's upload portal
// before the first real Jan–Dec cycle.
//
// It still renders through Cp8dTxt, the same code the real downloads use, so
// the pipe-delimited column contract is defined exactly once.
public class Cp8dConvertRequestDto
{
    // The E-number as typed; punctuation and letters are stripped server-side,
    // since LHDN's M/P filenames are built from the digits alone.
    [Required, MaxLength(40)]
    public string EmployerNo { get; set; } = string.Empty;

    [Required, MaxLength(200)]
    public string EmployerName { get; set; } = string.Empty;

    [Range(2000, 2100)]
    public int Year { get; set; }

    [Required, MinLength(1, ErrorMessage = "Add at least one employee row.")]
    public List<Cp8dConvertRowDto> Employees { get; set; } = [];
}

public class Cp8dConvertRowDto
{
    [Required, MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    // Income tax reference and IC are what LHDN matches the row against, so a
    // row without them has nothing to attach to and is rejected rather than
    // filed empty.
    [Required, MaxLength(40)]
    public string TaxRef { get; set; } = string.Empty;

    [Required, MaxLength(40)]
    public string NewIc { get; set; } = string.Empty;

    // "1" single · "2" married, sole earner · "3" both working / divorced /
    // widowed / single with children.
    [RegularExpression("^[123]$", ErrorMessage = "Category must be 1, 2 or 3.")]
    public string Category { get; set; } = "1";

    public bool TaxBorneByEmployer { get; set; }

    [Range(0, 50)]
    public int Children { get; set; }

    [Range(0, 100_000_000)]
    public decimal ChildRelief { get; set; }

    [Range(0, 100_000_000)]
    public decimal AnnualGross { get; set; }

    [Range(0, 100_000_000)]
    public decimal Epf { get; set; }

    [Range(0, 100_000_000)]
    public decimal Pcb { get; set; }
}
