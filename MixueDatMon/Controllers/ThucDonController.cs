using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MixueDatMon.DichVu;
using MixueDatMon.DuLieu;
using MixueDatMon.MoHinh.VM;

namespace MixueDatMon.Controllers;

public class ThucDonController : Controller
{
    private readonly DuLieuContext _csdl;
    private readonly DichVuCaiDat _caiDat;
    private readonly DichVuKho _kho;
    public ThucDonController(DuLieuContext csdl, DichVuCaiDat caiDat, DichVuKho kho) { _csdl = csdl; _caiDat = caiDat; _kho = kho; }

    /// <summary>Khách quét QR trên bàn -> /t/{maQr}: ghi nhớ bàn rồi chuyển tới thực đơn.</summary>
    public async Task<IActionResult> Ban(string maQr)
    {
        var ban = await _csdl.BanAn.AsNoTracking().FirstOrDefaultAsync(b => b.MaQr == maQr && b.DangHoatDong);
        if (ban == null)
        {
            TempData["CanhBao"] = "Mã QR không hợp lệ hoặc bàn đã ngừng sử dụng. Bạn vẫn có thể đặt món mang đi.";
            return RedirectToAction(nameof(Index));
        }
        HttpContext.Session.SetInt32("MaBan", ban.MaBan);
        HttpContext.Session.SetString("TenBan", ban.TenBan);
        return RedirectToAction(nameof(Index));
    }

    /// <summary>Bỏ bàn đã quét, chuyển sang đặt mang đi.</summary>
    public IActionResult MangDi()
    {
        HttpContext.Session.Remove("MaBan");
        HttpContext.Session.Remove("TenBan");
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Index()
    {
        var (duocDat, thongBaoDong) = await _caiDat.KiemTraNhanDonAsync();
        var caiDat = await _caiDat.LayAsync();
        var (monHet, toppingHet) = caiDat.TuAnMonHetNguyenLieu ? await _kho.LayHetNguyenLieuAsync() : (new HashSet<int>(), new HashSet<int>());

        var vm = new ThucDonVM
        {
            DuocDatHang = duocDat,
            ThongBaoDongCua = thongBaoDong,
            MonHetNguyenLieu = monHet,
            ToppingHetNguyenLieu = toppingHet,
            DanhSachNhom = await _csdl.NhomMon.AsNoTracking().Where(n => n.DangHoatDong).OrderBy(n => n.ThuTuHienThi)
                .Include(n => n.DanhSachMon.Where(m => m.DangBan).OrderByDescending(m => m.LaBanChay).ThenBy(m => m.TenMon))
                .ToListAsync(),
            DanhSachTopping = await _csdl.Topping.AsNoTracking().Where(t => t.DangBan).OrderBy(t => t.MaTopping).ToListAsync(),
            DanhSachMucDa = await _csdl.MucDa.AsNoTracking().OrderBy(x => x.MaMucDa).ToListAsync(),
            DanhSachMucDuong = await _csdl.MucDuong.AsNoTracking().OrderBy(x => x.MaMucDuong).ToListAsync(),
            ThongKe = await _csdl.ThongKeDanhGia.AsNoTracking().ToDictionaryAsync(t => t.MaMonAn),
            TenBan = HttpContext.Session.GetString("TenBan")
        };
        return View(vm);
    }

    /// <summary>Dữ liệu cho hộp "Xem đánh giá" của một món: điểm, số lượt, phân bố sao và nhận xét gần đây.</summary>
    [HttpGet]
    public async Task<IActionResult> DanhGiaMon(int ma)
    {
        var thongKe = await _csdl.ThongKeDanhGia.AsNoTracking().FirstOrDefaultAsync(t => t.MaMonAn == ma);

        var phanBo = await _csdl.DanhGiaMon.AsNoTracking()
            .Where(d => !d.DaAn && d.ChiTietDonHang.MaMonAn == ma)
            .GroupBy(d => d.SoSao)
            .Select(g => new { soSao = g.Key, soLuot = g.Count() })
            .ToListAsync();

        var danhSach = await _csdl.DanhGiaMon.AsNoTracking()
            .Where(d => !d.DaAn && d.ChiTietDonHang.MaMonAn == ma && d.NhanXet != null)
            .OrderByDescending(d => d.NgayTao).Take(20)
            .Select(d => new
            {
                soSao = d.SoSao,
                nhanXet = d.NhanXet,
                ngayTao = d.NgayTao,
                tenKhach = d.ChiTietDonHang.DonHang.TenKhach
            })
            .ToListAsync();

        return Json(new
        {
            soLuot = thongKe?.SoLuot ?? 0,
            diemTrungBinh = thongKe?.DiemTrungBinh ?? 0m,
            phanBo,
            danhSach
        });
    }
}
