using System.Text;
using BookManagement.Data;
using BookManagement.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

public class BukusController : Controller
{
    private readonly ApplicationDbContext _context;

    public BukusController(ApplicationDbContext context) => _context = context;

    public async Task<IActionResult> Index(string? search)
    {
        ViewData["Search"] = search;
        var query = _context.Bukus.Include(b => b.JenisBukuNavigation).AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(b => b.Judul.Contains(search)
                || b.Penulis.Contains(search)
                || b.JenisBukuNavigation.JenisBuku.Contains(search));

        return View("~/Views/Buku/Index.cshtml", await query.OrderBy(b => b.Judul).ToListAsync());
    }

    public async Task<IActionResult> Export(string? search)
    {
        var query = _context.Bukus.Include(b => b.JenisBukuNavigation).AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(b => b.Judul.Contains(search)
                || b.Penulis.Contains(search)
                || b.JenisBukuNavigation.JenisBuku.Contains(search));

        var books = await query.OrderBy(b => b.Judul).ToListAsync();
        static string Csv(string? value) => "\"" + (value ?? "").Replace("\"", "\"\"") + "\"";
        var csv = new StringBuilder();
        csv.AppendLine("Judul,Penulis,Jenis Buku,Tanggal Rilis,Jumlah Halaman");
        foreach (var book in books)
            csv.AppendLine(string.Join(",", Csv(book.Judul), Csv(book.Penulis),
                Csv(book.JenisBukuNavigation?.JenisBuku),
                Csv(book.TanggalRilis.ToString("yyyy-MM-dd")), Csv(book.JumlahHalaman.ToString())));

        return File(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv.ToString())).ToArray(),
            "text/csv; charset=utf-8", "buku.csv");
    }

    public async Task<IActionResult> Details(int? id)
    {
        if (id == null) return NotFound();
        var book = await _context.Bukus.Include(b => b.JenisBukuNavigation)
            .FirstOrDefaultAsync(b => b.Id == id);
        return book == null ? NotFound() : View("~/Views/Buku/Details.cshtml", book);
    }

    public async Task<IActionResult> Create()
    {
        await LoadJenisBuku();
        return View("~/Views/Buku/Create.cshtml");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create([Bind("Judul,Penulis,JenisBuku,TanggalRilis,JumlahHalaman")] Buku buku)
    {
        // EF-scaffolded required navigation properties are not posted by the form.
        // Remove that property from MVC validation; the foreign key is validated below.
        ModelState.Remove(nameof(Buku.JenisBukuNavigation));

        if (!await _context.MasterJenisBukus.AnyAsync(j => j.Id == buku.JenisBuku))
            ModelState.AddModelError(nameof(Buku.JenisBuku), "Pilih jenis buku yang valid.");

        if (ModelState.IsValid)
        {
            _context.Bukus.Add(buku);
            await _context.SaveChangesAsync();

            AddHistory(buku, "CREATE", null, Snapshot(buku));
            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(Index));
        }

        await LoadJenisBuku(buku.JenisBuku);
        return View("~/Views/Buku/Create.cshtml", buku);
    }

    public async Task<IActionResult> Edit(int? id)
    {
        if (id == null) return NotFound();
        var book = await _context.Bukus.FindAsync(id);
        if (book == null) return NotFound();
        await LoadJenisBuku(book.JenisBuku);
        return View("~/Views/Buku/Edit.cshtml", book);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, [Bind("Id,Judul,Penulis,JenisBuku,TanggalRilis,JumlahHalaman")] Buku input)
    {
        if (id != input.Id) return NotFound();
        var book = await _context.Bukus.FindAsync(id);
        if (book == null) return NotFound();

        ModelState.Remove(nameof(Buku.JenisBukuNavigation));
        if (!await _context.MasterJenisBukus.AnyAsync(j => j.Id == input.JenisBuku))
            ModelState.AddModelError(nameof(Buku.JenisBuku), "Pilih jenis buku yang valid.");

        if (ModelState.IsValid)
        {
            var oldData = Snapshot(book);
            book.Judul = input.Judul;
            book.Penulis = input.Penulis;
            book.JenisBuku = input.JenisBuku;
            book.TanggalRilis = input.TanggalRilis;
            book.JumlahHalaman = input.JumlahHalaman;

            AddHistory(book, "UPDATE", oldData, Snapshot(book));
            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(Index));
        }

        await LoadJenisBuku(input.JenisBuku);
        return View("~/Views/Buku/Edit.cshtml", input);
    }

    public async Task<IActionResult> Delete(int? id)
    {
        if (id == null) return NotFound();
        var book = await _context.Bukus.Include(b => b.JenisBukuNavigation)
            .FirstOrDefaultAsync(b => b.Id == id);
        return book == null ? NotFound() : View("~/Views/Buku/Delete.cshtml", book);
    }

    [HttpPost, ActionName("Delete"), ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteConfirmed(int id)
    {
        var book = await _context.Bukus.FindAsync(id);
        if (book != null)
        {
            AddHistory(book, "DELETE", Snapshot(book), null);
            _context.Bukus.Remove(book);
            await _context.SaveChangesAsync();
        }

        return RedirectToAction(nameof(Index));
    }

    private async Task LoadJenisBuku(int? selected = null)
    {
        ViewData["JenisBuku"] = new SelectList(
            await _context.MasterJenisBukus.OrderBy(j => j.JenisBuku).ToListAsync(),
            nameof(MasterJenisBuku.Id), nameof(MasterJenisBuku.JenisBuku), selected);
    }

    private void AddHistory(Buku book, string action, string? oldData, string? newData)
    {
        _context.HistoriBukus.Add(new HistoriBuku
        {
            BukuId = book.Id,
            Aksi = action,
            DataLama = oldData,
            DataBaru = newData,
            TanggalPerubahan = DateTime.Now
        });
    }

    private static string Snapshot(Buku book) =>
        System.Text.Json.JsonSerializer.Serialize(new
        {
            book.Judul, book.Penulis, book.JenisBuku, book.TanggalRilis, book.JumlahHalaman
        });
}
