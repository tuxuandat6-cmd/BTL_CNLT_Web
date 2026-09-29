using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MixueDatMon.DuLieu;
using MixueDatMon.MoHinh;
using MixueDatMon.MoHinh.VM;
using MixueDatMon.TroGiup;
using QRCoder;
using MixueDatMon.DichVu;

namespace MixueDatMon.Controllers;

/// <summary>Quản lý: toàn quyền hệ thống + quản lý kho.</summary>
[Authorize(Roles = "QuanLy")]
public class QuanLyController : Controller
{
    private readonly DuLieuContext _csdl;
    private readonly IWebHostEnvironment _moiTruong;
    private readonly IConfiguration _cauHinh;
    private readonly DichVuNhatKy _nhatKy;
    public QuanLyController(DuLieuContext csdl, IWebHostEnvironment moiTruong, IConfiguration cauHinh, DichVuNhatKy nhatKy)
    { _csdl = csdl; _moiTruong = moiTruong; _cauHinh = cauHinh; _nhatKy = nhatKy; }

    private int MaNguoiDungHienTai => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    // ================= TỔNG QUAN =================
    public async Task<IActionResult> TongQuan()
    {
        var homNay = DateTime.Today;
        var dauTuan = homNay.AddDays(-6);
        var dauThang = new DateTime(homNay.Year, homNay.Month, 1);
        var tuNgay = dauThang < dauTuan ? dauThang : dauTuan;

        var danhSachDaThu = await (from d in _csdl.DonHang
                                   join t in _csdl.TongTienDon on d.MaDonHang equals t.MaDonHang
                                   where d.NgayThanhToan != null && d.NgayThanhToan >= tuNgay && d.MaTrangThai != HangSoTrangThai.DaHuy
                                   select new { NgayThanhToan = d.NgayThanhToan!.Value, t.TongTien }).ToListAsync();

        var moc30Ngay = homNay.AddDays(-30);
        var monBanChay = await _csdl.ChiTietDonHang
            .Where(c => c.DonHang.NgayThanhToan >= moc30Ngay && c.DonHang.MaTrangThai != HangSoTrangThai.DaHuy)
            .GroupBy(c => c.MonAn.TenMon)
            .Select(g => new { Ten = g.Key, SoLuong = g.Sum(x => x.SoLuong) })
            .OrderByDescending(x => x.SoLuong).Take(5).ToListAsync();

        var soDanhGiaDon = await _csdl.DanhGiaDonHang.CountAsync();
        var diemDichVu = soDanhGiaDon == 0 ? 0 : await _csdl.DanhGiaDonHang.AverageAsync(d => (double)d.SoSao);

        var vm = new TongQuanVM
        {
            SoDanhGiaDichVu = soDanhGiaDon,
            DiemDichVu = Math.Round((decimal)diemDichVu, 1),
            DoanhThuHomNay = danhSachDaThu.Where(r => r.NgayThanhToan >= homNay).Sum(r => r.TongTien),
            DoanhThuTuan = danhSachDaThu.Where(r => r.NgayThanhToan >= dauTuan).Sum(r => r.TongTien),
            DoanhThuThang = danhSachDaThu.Where(r => r.NgayThanhToan >= dauThang).Sum(r => r.TongTien),
            SoDonHomNay = await _csdl.DonHang.CountAsync(d => d.NgayTao >= homNay && d.MaTrangThai != HangSoTrangThai.DaHuy),
            DonChoXacNhan = await _csdl.DonHang.CountAsync(d => d.MaTrangThai == HangSoTrangThai.ChoXacNhan),
            DoanhThu7Ngay = Enumerable.Range(0, 7).Select(i =>
            {
                var ngay = dauTuan.AddDays(i);
                return (ngay.ToString("dd/MM"), danhSachDaThu.Where(r => r.NgayThanhToan.Date == ngay).Sum(r => r.TongTien));
            }).ToList(),
            MonBanChay = monBanChay.Select(x => (x.Ten, x.SoLuong)).ToList(),
            NguyenLieuSapHet = await _csdl.NguyenLieu.AsNoTracking()
                .Where(n => n.DangHoatDong && n.SoLuongTon <= n.SoLuongToiThieu).OrderBy(n => n.SoLuongTon).ToListAsync()
        };
        return View(vm);
    }

    // ================= ĐÁNH GIÁ CỦA KHÁCH =================
    public async Task<IActionResult> DanhGia(int? toiDaSao)
    {
        var soDon = await _csdl.DanhGiaDonHang.CountAsync();
        var diemDon = soDon == 0 ? 0 : await _csdl.DanhGiaDonHang.AverageAsync(d => (double)d.SoSao);
        var soMon = await _csdl.DanhGiaMon.CountAsync(d => !d.DaAn);
        var diemMon = soMon == 0 ? 0 : await _csdl.DanhGiaMon.Where(d => !d.DaAn).AverageAsync(d => (double)d.SoSao);

        var thongKeMon = await (from t in _csdl.ThongKeDanhGia
                                join m in _csdl.MonAn on t.MaMonAn equals m.MaMonAn
                                orderby t.DiemTrungBinh, t.SoLuot descending
                                select new DongThongKeMonVM { TenMon = m.TenMon, SoLuot = t.SoLuot, DiemTrungBinh = t.DiemTrungBinh })
                               .ToListAsync();

        var truyVanGopY = _csdl.DanhGiaDonHang.AsQueryable();
        if (toiDaSao != null) truyVanGopY = truyVanGopY.Where(d => d.SoSao <= toiDaSao);
        var danhSachGopY = await truyVanGopY.OrderByDescending(d => d.NgayTao).Take(100)
            .Select(d => new DongGopYVM
            {
                MaDanhGiaDon = d.MaDanhGiaDon, MaTheoDoi = d.DonHang.MaTheoDoi,
                NoiPhucVu = d.DonHang.BanAn != null ? d.DonHang.BanAn.TenBan : "Mang đi",
                SoSao = d.SoSao, GopY = d.GopY, NgayTao = d.NgayTao, DaXem = d.DaXem
            }).ToListAsync();

        var truyVanNhanXet = _csdl.DanhGiaMon.AsQueryable();
        if (toiDaSao != null) truyVanNhanXet = truyVanNhanXet.Where(d => d.SoSao <= toiDaSao);
        var danhSachNhanXet = await truyVanNhanXet.OrderByDescending(d => d.NgayTao).Take(100)
            .Select(d => new DongNhanXetMonVM
            {
                MaDanhGiaMon = d.MaDanhGiaMon, TenMon = d.ChiTietDonHang.MonAn.TenMon,
                MaTheoDoi = d.ChiTietDonHang.DonHang.MaTheoDoi, TenKhach = d.ChiTietDonHang.DonHang.TenKhach,
                SoSao = d.SoSao, NhanXet = d.NhanXet, NgayTao = d.NgayTao, DaAn = d.DaAn
            }).ToListAsync();

        return View(new DanhGiaVM
        {
            SoDanhGiaDon = soDon, DiemDichVu = Math.Round((decimal)diemDon, 1),
            SoDanhGiaMon = soMon, DiemMon = Math.Round((decimal)diemMon, 1),
            SoDanhGiaThap = await _csdl.DanhGiaDonHang.CountAsync(d => d.SoSao <= 2 && !d.DaXem),
            ToiDaSao = toiDaSao, ThongKeMon = thongKeMon, DanhSachGopY = danhSachGopY, DanhSachNhanXet = danhSachNhanXet
        });
    }

    /// <summary>Ẩn / hiện một nhận xét về món (nhận xét bị ẩn không tính vào điểm và không hiện cho khách).</summary>
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> AnHienDanhGiaMon(int ma, int? toiDaSao)
    {
        var danhGia = await _csdl.DanhGiaMon.FindAsync(ma);
        if (danhGia == null) return NotFound();
        danhGia.DaAn = !danhGia.DaAn;
        await _csdl.SaveChangesAsync();
        return RedirectToAction(nameof(DanhGia), new { toiDaSao });
    }

    /// <summary>Đánh dấu góp ý đã xem / chưa xem.</summary>
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> DoiTrangThaiGopY(int ma, int? toiDaSao)
    {
        var danhGia = await _csdl.DanhGiaDonHang.FindAsync(ma);
        if (danhGia == null) return NotFound();
        danhGia.DaXem = !danhGia.DaXem;
        await _csdl.SaveChangesAsync();
        return RedirectToAction(nameof(DanhGia), new { toiDaSao });
    }

    // ================= MÓN ĂN =================
    public async Task<IActionResult> DanhSachMon() =>
        View(await _csdl.MonAn.AsNoTracking().Include(m => m.NhomMon)
            .OrderBy(m => m.NhomMon.ThuTuHienThi).ThenBy(m => m.TenMon).ToListAsync());

    [HttpGet]
    public async Task<IActionResult> FormMon(int? ma)
    {
        var vm = new FormMonVM { DanhSachNhom = await _csdl.NhomMon.AsNoTracking().OrderBy(n => n.ThuTuHienThi).ToListAsync() };
        if (ma != null)
        {
            var mon = await _csdl.MonAn.AsNoTracking().FirstOrDefaultAsync(m => m.MaMonAn == ma);
            if (mon == null) return NotFound();
            vm.MaMonAn = mon.MaMonAn; vm.MaNhomMon = mon.MaNhomMon; vm.TenMon = mon.TenMon; vm.TenTiengAnh = mon.TenTiengAnh;
            vm.MoTa = mon.MoTa; vm.Gia = mon.Gia; vm.DuongDanAnh = mon.DuongDanAnh; vm.LaBanChay = mon.LaBanChay; vm.DangBan = mon.DangBan;
        }
        return View(vm);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> FormMon(FormMonVM vm)
    {
        vm.DanhSachNhom = await _csdl.NhomMon.AsNoTracking().OrderBy(n => n.ThuTuHienThi).ToListAsync();

        string? duongDanAnhMoi = null;
        if (vm.TepAnh is { Length: > 0 })
        {
            var duoiTep = Path.GetExtension(vm.TepAnh.FileName).ToLowerInvariant();
            if (!new[] { ".jpg", ".jpeg", ".png", ".webp" }.Contains(duoiTep) || vm.TepAnh.Length > 2 * 1024 * 1024)
                ModelState.AddModelError(nameof(vm.TepAnh), "Chỉ nhận ảnh .jpg, .png, .webp dưới 2MB.");
            else
            {
                var thuMuc = Path.Combine(_moiTruong.WebRootPath, "uploads");
                Directory.CreateDirectory(thuMuc);
                var tenTep = Guid.NewGuid().ToString("N") + duoiTep;
                await using var luong = System.IO.File.Create(Path.Combine(thuMuc, tenTep));
                await vm.TepAnh.CopyToAsync(luong);
                duongDanAnhMoi = "/uploads/" + tenTep;
            }
        }
        if (!ModelState.IsValid) return View(vm);

        MonAn mon;
        if (vm.MaMonAn == 0) { mon = new MonAn(); _csdl.MonAn.Add(mon); }
        else mon = await _csdl.MonAn.FindAsync(vm.MaMonAn) ?? throw new InvalidOperationException("Không tìm thấy món");

        mon.MaNhomMon = vm.MaNhomMon; mon.TenMon = vm.TenMon.Trim(); mon.TenTiengAnh = vm.TenTiengAnh?.Trim();
        mon.MoTa = vm.MoTa?.Trim(); mon.Gia = vm.Gia; mon.LaBanChay = vm.LaBanChay; mon.DangBan = vm.DangBan;
        if (duongDanAnhMoi != null) mon.DuongDanAnh = duongDanAnhMoi;

        try { await _csdl.SaveChangesAsync(); }
        catch (DbUpdateException)
        {
            ModelState.AddModelError(nameof(vm.TenMon), "Tên món đã tồn tại trong nhóm này.");
            return View(vm);
        }
        TempData["ThanhCong"] = "Đã lưu món.";
        return RedirectToAction(nameof(DanhSachMon));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> DoiTrangThaiMon(int ma, string truong)
    {
        var mon = await _csdl.MonAn.FindAsync(ma);
        if (mon == null) return NotFound();
        if (truong == "dangban") mon.DangBan = !mon.DangBan;
        else if (truong == "banchay") mon.LaBanChay = !mon.LaBanChay;
        await _csdl.SaveChangesAsync();
        return RedirectToAction(nameof(DanhSachMon));
    }

    // ---- Công thức (định mức nguyên liệu) của 1 món ----
    [HttpGet]
    public async Task<IActionResult> CongThuc(int ma)
    {
        var mon = await _csdl.MonAn.AsNoTracking().FirstOrDefaultAsync(m => m.MaMonAn == ma);
        if (mon == null) return NotFound();
        return View(new CongThucVM
        {
            MonAn = mon,
            DanhSachNguyenLieu = await _csdl.NguyenLieu.AsNoTracking().Where(n => n.DangHoatDong).OrderBy(n => n.TenNguyenLieu).ToListAsync(),
            DangDung = await _csdl.CongThucMon.AsNoTracking().Where(r => r.MaMonAn == ma).ToDictionaryAsync(r => r.MaNguyenLieu, r => r.SoLuongDung)
        });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> CongThuc(int ma, List<int> danhSachMaNguyenLieu, List<decimal?> danhSachSoLuong)
    {
        var hienCo = await _csdl.CongThucMon.Where(r => r.MaMonAn == ma).ToListAsync();
        for (var i = 0; i < danhSachMaNguyenLieu.Count && i < danhSachSoLuong.Count; i++)
        {
            var maNguyenLieu = danhSachMaNguyenLieu[i];
            var soLuong = danhSachSoLuong[i] ?? 0;
            var dong = hienCo.FirstOrDefault(r => r.MaNguyenLieu == maNguyenLieu);
            if (soLuong > 0)
            {
                if (dong == null) _csdl.CongThucMon.Add(new CongThucMon { MaMonAn = ma, MaNguyenLieu = maNguyenLieu, SoLuongDung = soLuong });
                else dong.SoLuongDung = soLuong;
            }
            else if (dong != null) _csdl.CongThucMon.Remove(dong);
        }
        await _csdl.SaveChangesAsync();
        TempData["ThanhCong"] = "Đã lưu công thức.";
        return RedirectToAction(nameof(DanhSachMon));
    }

    // ---- Topping ----
    public async Task<IActionResult> DanhSachTopping() =>
        View(await _csdl.Topping.AsNoTracking().OrderBy(t => t.MaTopping).ToListAsync());

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> LuuTopping(int maTopping, string tenTopping, decimal gia, bool dangBan)
    {
        if (string.IsNullOrWhiteSpace(tenTopping) || gia < 0)
        { TempData["CanhBao"] = "Tên topping và giá không hợp lệ."; return RedirectToAction(nameof(DanhSachTopping)); }
        var topping = maTopping == 0 ? new Topping() : await _csdl.Topping.FindAsync(maTopping);
        if (topping == null) return NotFound();
        topping.TenTopping = tenTopping.Trim(); topping.Gia = gia; topping.DangBan = dangBan;
        if (maTopping == 0) _csdl.Topping.Add(topping);
        try { await _csdl.SaveChangesAsync(); TempData["ThanhCong"] = "Đã lưu topping."; }
        catch (DbUpdateException) { TempData["CanhBao"] = "Tên topping đã tồn tại."; }
        return RedirectToAction(nameof(DanhSachTopping));
    }

    // ================= NHÂN VIÊN =================
    public async Task<IActionResult> NhanVien() =>
        View(await _csdl.NguoiDung.AsNoTracking().Include(n => n.VaiTro).OrderBy(n => n.MaVaiTro).ThenBy(n => n.TenDangNhap).ToListAsync());

    [HttpGet]
    public async Task<IActionResult> FormNhanVien() =>
        View(new FormNhanVienVM { DanhSachVaiTro = await _csdl.VaiTro.AsNoTracking().ToListAsync() });

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> FormNhanVien(FormNhanVienVM vm)
    {
        vm.DanhSachVaiTro = await _csdl.VaiTro.AsNoTracking().ToListAsync();
        if (await _csdl.NguoiDung.AnyAsync(n => n.TenDangNhap == vm.TenDangNhap.Trim()))
            ModelState.AddModelError(nameof(vm.TenDangNhap), "Tên đăng nhập đã tồn tại.");
        if (!vm.DanhSachVaiTro.Any(v => v.MaVaiTro == vm.MaVaiTro))
            ModelState.AddModelError(nameof(vm.MaVaiTro), "Chọn vai trò hợp lệ.");
        if (!ModelState.IsValid) return View(vm);

        var muoi = MaHoaMatKhau.TaoMuoi();
        _csdl.NguoiDung.Add(new NguoiDung
        {
            TenDangNhap = vm.TenDangNhap.Trim(), HoTen = vm.HoTen.Trim(), SoDienThoai = vm.SoDienThoai?.Trim(), MaVaiTro = vm.MaVaiTro,
            MuoiMatKhau = muoi, MatKhauHash = MaHoaMatKhau.Bam(vm.MatKhau, muoi)
        });
        _nhatKy.Ghi(HangSoNhatKy.TaoTaiKhoan, $"Tạo tài khoản \"{vm.TenDangNhap.Trim()}\" ({vm.HoTen.Trim()}), vai trò {vm.DanhSachVaiTro.First(v => v.MaVaiTro == vm.MaVaiTro).TenVaiTro}.", MaNguoiDungHienTai);
        await _csdl.SaveChangesAsync();
        TempData["ThanhCong"] = "Đã tạo tài khoản.";
        return RedirectToAction(nameof(NhanVien));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> DatLaiMatKhau(int ma, string matKhauMoi)
    {
        if (string.IsNullOrEmpty(matKhauMoi) || matKhauMoi.Length < 8)
        {
            TempData["CanhBao"] = "Mật khẩu mới tối thiểu 8 ký tự.";
            return RedirectToAction(nameof(NhanVien));
        }
        var nguoiDung = await _csdl.NguoiDung.FindAsync(ma);
        if (nguoiDung == null) return NotFound();
        nguoiDung.MuoiMatKhau = MaHoaMatKhau.TaoMuoi();
        nguoiDung.MatKhauHash = MaHoaMatKhau.Bam(matKhauMoi, nguoiDung.MuoiMatKhau);
        _nhatKy.Ghi(HangSoNhatKy.DatLaiMatKhau, $"Đặt lại mật khẩu cho tài khoản \"{nguoiDung.TenDangNhap}\".", MaNguoiDungHienTai);
        await _csdl.SaveChangesAsync();
        TempData["ThanhCong"] = $"Đã đặt lại mật khẩu cho {nguoiDung.TenDangNhap}.";
        return RedirectToAction(nameof(NhanVien));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> KhoaMoNhanVien(int ma)
    {
        if (ma == MaNguoiDungHienTai) { TempData["CanhBao"] = "Không thể khóa chính tài khoản đang đăng nhập."; return RedirectToAction(nameof(NhanVien)); }
        var nguoiDung = await _csdl.NguoiDung.FindAsync(ma);
        if (nguoiDung == null) return NotFound();
        nguoiDung.DangHoatDong = !nguoiDung.DangHoatDong;
        _nhatKy.Ghi(HangSoNhatKy.KhoaMoTaiKhoan, $"{(nguoiDung.DangHoatDong ? "Mở lại" : "Khóa")} tài khoản \"{nguoiDung.TenDangNhap}\".", MaNguoiDungHienTai);
        await _csdl.SaveChangesAsync();
        return RedirectToAction(nameof(NhanVien));
    }

    // ================= KHO =================
    public async Task<IActionResult> Kho() =>
        View(await _csdl.NguyenLieu.AsNoTracking().OrderBy(n => n.TenNguyenLieu).ToListAsync());

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ThemNguyenLieu(string tenNguyenLieu, string donViTinh, decimal soLuongToiThieu)
    {
        if (string.IsNullOrWhiteSpace(tenNguyenLieu) || string.IsNullOrWhiteSpace(donViTinh) || soLuongToiThieu < 0)
        { TempData["CanhBao"] = "Nhập đủ tên, đơn vị và mức tối thiểu hợp lệ."; return RedirectToAction(nameof(Kho)); }
        _csdl.NguyenLieu.Add(new NguyenLieu { TenNguyenLieu = tenNguyenLieu.Trim(), DonViTinh = donViTinh.Trim(), SoLuongToiThieu = soLuongToiThieu });
        try { await _csdl.SaveChangesAsync(); TempData["ThanhCong"] = "Đã thêm nguyên liệu."; }
        catch (DbUpdateException) { TempData["CanhBao"] = "Nguyên liệu này đã tồn tại."; }
        return RedirectToAction(nameof(Kho));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> DatMucToiThieu(int ma, decimal soLuongToiThieu)
    {
        var nguyenLieu = await _csdl.NguyenLieu.FindAsync(ma);
        if (nguyenLieu == null || soLuongToiThieu < 0) return BadRequest();
        nguyenLieu.SoLuongToiThieu = soLuongToiThieu;
        await _csdl.SaveChangesAsync();
        return RedirectToAction(nameof(Kho));
    }

    /// <summary>Kiểm kê / hao hụt: cộng hoặc trừ tồn kho kèm lý do.</summary>
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> DieuChinhTon(int ma, decimal chenhLech, string? ghiChu)
    {
        var nguyenLieu = await _csdl.NguyenLieu.FindAsync(ma);
        if (nguyenLieu == null || chenhLech == 0) return RedirectToAction(nameof(Kho));
        nguyenLieu.SoLuongTon += chenhLech;
        _csdl.BienDongKho.Add(new BienDongKho
        {
            MaNguyenLieu = ma, SoLuongThayDoi = chenhLech, LoaiBienDong = HangSoBienDong.DieuChinh,
            MaNguoiThucHien = MaNguoiDungHienTai, GhiChu = string.IsNullOrWhiteSpace(ghiChu) ? "Điều chỉnh kiểm kê" : ghiChu.Trim()
        });
        _nhatKy.Ghi(HangSoNhatKy.DieuChinhKho, $"Điều chỉnh {nguyenLieu.TenNguyenLieu}: {(chenhLech > 0 ? "+" : "")}{chenhLech.SoThapPhan()} {nguyenLieu.DonViTinh}. Lý do: {(string.IsNullOrWhiteSpace(ghiChu) ? "Điều chỉnh kiểm kê" : ghiChu.Trim())}.", MaNguoiDungHienTai);
        await _csdl.SaveChangesAsync();
        TempData["ThanhCong"] = "Đã điều chỉnh tồn kho.";
        return RedirectToAction(nameof(Kho));
    }

    [HttpGet]
    public async Task<IActionResult> NhapKho()
    {
        ViewBag.DanhSachNguyenLieu = await _csdl.NguyenLieu.AsNoTracking().Where(n => n.DangHoatDong).OrderBy(n => n.TenNguyenLieu).ToListAsync();
        ViewBag.DanhSachNhaCungCap = await _csdl.NhaCungCap.AsNoTracking().OrderBy(n => n.TenNhaCungCap).ToListAsync();
        return View(new FormNhapKhoVM());
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> NhapKho(FormNhapKhoVM vm)
    {
        var danhSachDong = new List<(int MaNguyenLieu, decimal SoLuong, decimal DonGia)>();
        for (var i = 0; i < vm.DanhSachMaNguyenLieu.Count; i++)
        {
            var soLuong = i < vm.DanhSachSoLuong.Count ? vm.DanhSachSoLuong[i] : 0;
            var donGia = i < vm.DanhSachDonGia.Count ? vm.DanhSachDonGia[i] : 0;
            if (soLuong > 0 && donGia >= 0) danhSachDong.Add((vm.DanhSachMaNguyenLieu[i], soLuong, donGia));
        }
        if (danhSachDong.Count == 0) { TempData["CanhBao"] = "Phiếu nhập cần ít nhất 1 dòng có số lượng > 0."; return RedirectToAction(nameof(NhapKho)); }

        NhaCungCap? nhaCungCap = null;
        if (!string.IsNullOrWhiteSpace(vm.TenNhaCungCap))
        {
            var ten = vm.TenNhaCungCap.Trim();
            nhaCungCap = await _csdl.NhaCungCap.FirstOrDefaultAsync(n => n.TenNhaCungCap == ten) ?? new NhaCungCap { TenNhaCungCap = ten };
        }

        var phieuNhap = new PhieuNhap { NhaCungCap = nhaCungCap, MaNguoiNhap = MaNguoiDungHienTai, GhiChu = vm.GhiChu?.Trim(), NgayNhap = DateTime.Now };
        var danhSachMa = danhSachDong.Select(d => d.MaNguyenLieu).Distinct().ToList();
        var nguyenLieu = await _csdl.NguyenLieu.Where(n => danhSachMa.Contains(n.MaNguyenLieu)).ToDictionaryAsync(n => n.MaNguyenLieu);

        await using var giaoDich = await _csdl.Database.BeginTransactionAsync();
        _csdl.PhieuNhap.Add(phieuNhap);
        await _csdl.SaveChangesAsync();   // lấy MaPhieuNhap

        foreach (var (maNguyenLieu, soLuong, donGia) in danhSachDong)
        {
            if (!nguyenLieu.TryGetValue(maNguyenLieu, out var nl)) continue;
            phieuNhap.DanhSachChiTiet.Add(new ChiTietPhieuNhap { MaNguyenLieu = maNguyenLieu, SoLuong = soLuong, DonGiaNhap = donGia });
            nl.SoLuongTon += soLuong;
            _csdl.BienDongKho.Add(new BienDongKho
            {
                MaNguyenLieu = maNguyenLieu, SoLuongThayDoi = soLuong, LoaiBienDong = HangSoBienDong.Nhap,
                MaPhieuNhap = phieuNhap.MaPhieuNhap, MaNguoiThucHien = MaNguoiDungHienTai, GhiChu = "Nhập kho"
            });
        }
        _nhatKy.Ghi(HangSoNhatKy.NhapKho, $"Nhập kho {danhSachDong.Count} dòng nguyên liệu" + (nhaCungCap != null ? $" từ {nhaCungCap.TenNhaCungCap}" : "") + ".", MaNguoiDungHienTai);
        await _csdl.SaveChangesAsync();
        await giaoDich.CommitAsync();
        TempData["ThanhCong"] = "Đã nhập kho và cập nhật tồn.";
        return RedirectToAction(nameof(Kho));
    }

    public async Task<IActionResult> SoKho() =>
        View(await _csdl.BienDongKho.AsNoTracking().Include(b => b.NguyenLieu)
            .OrderByDescending(b => b.MaBienDong).Take(200).ToListAsync());

    // ================= BÀN & QR =================
    public async Task<IActionResult> DanhSachBan() =>
        View(await _csdl.BanAn.AsNoTracking().OrderBy(b => b.MaBan).ToListAsync());

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ThemBan(string tenBan)
    {
        if (string.IsNullOrWhiteSpace(tenBan)) return RedirectToAction(nameof(DanhSachBan));
        _csdl.BanAn.Add(new BanAn { TenBan = tenBan.Trim(), MaQr = TaoMaQr() });
        try { await _csdl.SaveChangesAsync(); TempData["ThanhCong"] = "Đã thêm bàn."; }
        catch (DbUpdateException) { TempData["CanhBao"] = "Tên bàn đã tồn tại."; }
        return RedirectToAction(nameof(DanhSachBan));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> KhoaMoBan(int ma)
    {
        var ban = await _csdl.BanAn.FindAsync(ma);
        if (ban == null) return NotFound();
        ban.DangHoatDong = !ban.DangHoatDong;
        await _csdl.SaveChangesAsync();
        return RedirectToAction(nameof(DanhSachBan));
    }

    /// <summary>Đổi mã khi QR bị lộ / cần vô hiệu hóa QR cũ (phải in lại).</summary>
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> DoiMaQr(int ma)
    {
        var ban = await _csdl.BanAn.FindAsync(ma);
        if (ban == null) return NotFound();
        ban.MaQr = TaoMaQr();
        _nhatKy.Ghi(HangSoNhatKy.DoiMaQr, $"Đổi mã QR của {ban.TenBan} (mã cũ ngừng dùng).", MaNguoiDungHienTai);
        await _csdl.SaveChangesAsync();
        TempData["ThanhCong"] = $"{ban.TenBan}: mã QR mới đã sẵn sàng, mã cũ không còn dùng được.";
        return RedirectToAction(nameof(DanhSachBan));
    }

    public async Task<IActionResult> AnhQrBan(int ma)
    {
        var ban = await _csdl.BanAn.AsNoTracking().FirstOrDefaultAsync(b => b.MaBan == ma);
        if (ban == null) return NotFound();
        var diaChiGoc = _cauHinh["CuaHang:DiaChiCongKhai"];
        if (string.IsNullOrWhiteSpace(diaChiGoc)) diaChiGoc = $"{Request.Scheme}://{Request.Host}";
        using var boTaoQr = new QRCodeGenerator();
        using var duLieuQr = boTaoQr.CreateQrCode($"{diaChiGoc.TrimEnd('/')}/t/{ban.MaQr}", QRCodeGenerator.ECCLevel.Q);
        var anhPng = new PngByteQRCode(duLieuQr).GetGraphic(12);
        return File(anhPng, "image/png");
    }

    public async Task<IActionResult> InQr() =>
        View(await _csdl.BanAn.AsNoTracking().Where(b => b.DangHoatDong).OrderBy(b => b.MaBan).ToListAsync());

    private static string TaoMaQr() => Convert.ToHexString(RandomNumberGenerator.GetBytes(16));

    // ================= LỊCH SỬ ĐƠN =================
    public async Task<IActionResult> LichSuDon(DateTime? tuNgay, DateTime? denNgay, int? trangThai)
    {
        var truyVan = _csdl.DonHang.AsQueryable();
        if (tuNgay != null) truyVan = truyVan.Where(d => d.NgayTao >= tuNgay.Value.Date);
        if (denNgay != null) truyVan = truyVan.Where(d => d.NgayTao < denNgay.Value.Date.AddDays(1));
        if (trangThai != null) truyVan = truyVan.Where(d => d.MaTrangThai == trangThai);

        var danhSachDong = await (from d in truyVan
                                  join t in _csdl.TongTienDon on d.MaDonHang equals t.MaDonHang
                                  orderby d.NgayTao descending
                                  select new DongDonHangVM
                                  {
                                      MaDonHang = d.MaDonHang, MaTheoDoi = d.MaTheoDoi, MaTrangThai = d.MaTrangThai, TongTien = t.TongTien,
                                      NgayTao = d.NgayTao, NgayThanhToan = d.NgayThanhToan,
                                      NoiPhucVu = d.BanAn != null ? d.BanAn.TenBan : "Mang đi",
                                      NguoiXuLy = d.NguoiXuLy != null ? d.NguoiXuLy.HoTen : null
                                  }).Take(300).ToListAsync();

        return View(new LichSuDonVM { DanhSachDong = danhSachDong, TuNgay = tuNgay, DenNgay = denNgay, TrangThai = trangThai });
    }

    // ================= CÀI ĐẶT QUÁN =================
    [HttpGet]
    public async Task<IActionResult> CaiDat() =>
        View(await _csdl.CaiDatCuaHang.AsNoTracking().FirstOrDefaultAsync() ?? new CaiDatCuaHang());

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> CaiDat(CaiDatCuaHang vm)
    {
        if (vm.SoPhutTuHuyDonMangDi < 0 || vm.SoPhutTuHuyDonMangDi > 240)
        {
            TempData["CanhBao"] = "Số phút tự hủy đơn phải từ 0 đến 240.";
            return RedirectToAction(nameof(CaiDat));
        }
        var caiDat = await _csdl.CaiDatCuaHang.FirstOrDefaultAsync() ?? new CaiDatCuaHang { MaCaiDat = 1 };
        if (caiDat.MaCaiDat == 0) { caiDat.MaCaiDat = 1; _csdl.CaiDatCuaHang.Add(caiDat); }

        var moTa = new List<string>();
        if (caiDat.GioMo != vm.GioMo || caiDat.GioDong != vm.GioDong) moTa.Add($"giờ mở cửa {vm.GioMo:hh\\:mm}-{vm.GioDong:hh\\:mm}");
        if (caiDat.TamNgungNhanDon != vm.TamNgungNhanDon) moTa.Add(vm.TamNgungNhanDon ? "tạm ngưng nhận đơn" : "mở lại nhận đơn");
        if (caiDat.SoPhutTuHuyDonMangDi != vm.SoPhutTuHuyDonMangDi) moTa.Add($"tự hủy đơn mang đi sau {vm.SoPhutTuHuyDonMangDi} phút" + (vm.SoPhutTuHuyDonMangDi == 0 ? " (đã tắt)" : ""));
        if (caiDat.TuAnMonHetNguyenLieu != vm.TuAnMonHetNguyenLieu) moTa.Add(vm.TuAnMonHetNguyenLieu ? "bật tự ẩn món hết nguyên liệu" : "tắt tự ẩn món hết nguyên liệu");

        caiDat.GioMo = vm.GioMo; caiDat.GioDong = vm.GioDong; caiDat.TamNgungNhanDon = vm.TamNgungNhanDon;
        caiDat.SoPhutTuHuyDonMangDi = vm.SoPhutTuHuyDonMangDi; caiDat.TuAnMonHetNguyenLieu = vm.TuAnMonHetNguyenLieu;
        if (moTa.Count > 0) _nhatKy.Ghi(HangSoNhatKy.SuaCaiDat, "Đổi cài đặt quán: " + string.Join(", ", moTa) + ".", MaNguoiDungHienTai);
        await _csdl.SaveChangesAsync();
        TempData["ThanhCong"] = "Đã lưu cài đặt quán.";
        return RedirectToAction(nameof(CaiDat));
    }

    // ================= NHẬT KÝ THAO TÁC =================
    public async Task<IActionResult> NhatKy(DateTime? tuNgay, DateTime? denNgay, string? hanhDong)
    {
        var truyVan = _csdl.NhatKyThaoTac.AsNoTracking().Include(n => n.NguoiThucHien).AsQueryable();
        if (tuNgay != null) truyVan = truyVan.Where(n => n.NgayTao >= tuNgay.Value.Date);
        if (denNgay != null) truyVan = truyVan.Where(n => n.NgayTao < denNgay.Value.Date.AddDays(1));
        if (!string.IsNullOrEmpty(hanhDong)) truyVan = truyVan.Where(n => n.HanhDong == hanhDong);

        var danhSach = await truyVan.OrderByDescending(n => n.NgayTao).Take(301).ToListAsync();
        var daCatBot = danhSach.Count > 300;
        if (daCatBot) danhSach = danhSach.Take(300).ToList();

        return View(new NhatKyVM { DanhSachDong = danhSach, TuNgay = tuNgay, DenNgay = denNgay, HanhDong = hanhDong, DaCatBot = daCatBot });
    }
}
