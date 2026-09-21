<#
.SYNOPSIS
    System Information - info.exe
    Giao diện bảng 2 cột cổ điển, hiển thị thông số chi tiết chuẩn xác Windows 10/11.
    Cửa sổ vừa khít dữ liệu (SizeToContent), cố định kích thước (không cho resize).
    Thứ tự: CPU -> RAM -> Graphics Card (nhóm 3 linh kiện highlight xanh) -> Storage.
#>

param([switch]$AsJson)

Add-Type -AssemblyName PresentationFramework, PresentationCore, WindowsBase, System.Drawing, System.Windows.Forms
if (-not $AsJson) {
# Đảm bảo chạy ở chế độ STA khi chạy trực tiếp từ script
if ($PSCommandPath -and [System.Threading.Thread]::CurrentThread.GetApartmentState() -ne [System.Threading.ApartmentState]::STA) {
    Start-Process powershell.exe -ArgumentList "-NoProfile -ExecutionPolicy Bypass -STA -File `"$PSCommandPath`"" -WindowStyle Normal
    exit
}

Add-Type -AssemblyName PresentationFramework, PresentationCore, WindowsBase, System.Drawing, System.Windows.Forms

}
# --- KÍCH HOẠT PER-MONITOR DPI AWARENESS V2 CHUẨN WINDOWS 10/11 ---
Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;

public class NativeDpiHelper {
    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool SetProcessDpiAwarenessContext(IntPtr dpiContext);

    [DllImport("user32.dll")]
    public static extern uint GetDpiForSystem();

    public static readonly IntPtr DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 = new IntPtr(-4);

    public static void EnablePerMonitorDpi() {
        try {
            SetProcessDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
        } catch {}
    }
}
"@ -ErrorAction SilentlyContinue

[NativeDpiHelper]::EnablePerMonitorDpi()

if (-not $AsJson) {
# --- MÀN HÌNH LOADING TỐI GIẢN KHÔNG VIỀN (KHỞI ĐỘNG TỨC THÌ <50MS) ---
$splashXaml = @'
<Window xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="System Information"
        Width="400" Height="110"
        WindowStartupLocation="CenterScreen"
        WindowStyle="None"
        ResizeMode="NoResize"
        AllowsTransparency="True"
        Background="Transparent"
        FontFamily="Segoe UI, Tahoma, Arial"
        Topmost="True">
    <Border Margin="10" Background="#FFFFFF" CornerRadius="8">
        <Border.Effect>
            <DropShadowEffect BlurRadius="12" ShadowDepth="2" Opacity="0.2" Color="#000000"/>
        </Border.Effect>
        <Grid Margin="18,14,18,14" VerticalAlignment="Center">
            <Grid.RowDefinitions>
                <RowDefinition Height="Auto"/>
                <RowDefinition Height="12"/>
                <RowDefinition Height="Auto"/>
            </Grid.RowDefinitions>

            <!-- Dòng 1: Tiêu đề tối giản -->
            <TextBlock Grid.Row="0" Text="Đang tải thông tin hệ thống:" FontSize="13" FontWeight="SemiBold" Foreground="#1E293B"/>

            <!-- Dòng 2: Progressbar và % ở dưới -->
            <Grid Grid.Row="2">
                <Grid.ColumnDefinitions>
                    <ColumnDefinition Width="*"/>
                    <ColumnDefinition Width="Auto"/>
                </Grid.ColumnDefinitions>
                <Border Grid.Column="0" Height="8" CornerRadius="4" Background="#E2E8F0" ClipToBounds="True" VerticalAlignment="Center">
                    <ProgressBar x:Name="progBar" Height="8" Minimum="0" Maximum="100" Value="1"
                                 Foreground="#0078D7" Background="Transparent" BorderThickness="0"/>
                </Border>
                <TextBlock x:Name="txtPercent" Grid.Column="1" Text="1%" FontSize="13" FontWeight="Bold" Foreground="#0078D7" Margin="12,0,0,0" VerticalAlignment="Center" Width="38" TextAlignment="Right"/>
            </Grid>
        </Grid>
    </Border>
</Window>
'@

$splashReader = [System.Xml.XmlReader]::Create([System.IO.StringReader]::new($splashXaml))
$splash = [System.Windows.Markup.XamlReader]::Load($splashReader)
$txtPercent = $splash.FindName("txtPercent")
$progBar = $splash.FindName("progBar")

$splash.Add_MouseLeftButtonDown({
    param($s, $e)
    if ($e.ButtonState -eq [System.Windows.Input.MouseButtonState]::Pressed) {
        $s.DragMove()
    }
})

try {
    $iconPath = $null
    if ($PSScriptRoot) {
        $candidate = Join-Path $PSScriptRoot "app.ico"
        if (Test-Path $candidate) { $iconPath = $candidate }
    }
    if (-not $iconPath) {
        $baseDir = [System.AppDomain]::CurrentDomain.BaseDirectory
        if ($baseDir) {
            $candidate = Join-Path $baseDir "app.ico"
            if (Test-Path $candidate) { $iconPath = $candidate }
        }
    }
    if ($iconPath -and (Test-Path $iconPath)) {
        $splash.Icon = [System.Windows.Media.Imaging.BitmapFrame]::Create([System.Uri]::new((Resolve-Path $iconPath).Path, [System.UriKind]::Absolute))
    }
} catch {}

$splash.Show()

function Update-SplashProgress($text, $val) {
    if ($txtPercent) { $txtPercent.Text = "$val%" }
    if ($progBar) { $progBar.Value = $val }
    [System.Windows.Threading.Dispatcher]::CurrentDispatcher.Invoke([Action]{}, [System.Windows.Threading.DispatcherPriority]::Render)
}

}

# Helper P/Invoke lấy chính xác tần số quét cao nhất của màn hình (Refresh Rate)
Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;

public class DisplayHelper {
    [StructLayout(LayoutKind.Sequential)]
    public struct DEVMODE {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string dmDeviceName;
        public short dmSpecVersion;
        public short dmDriverVersion;
        public short dmSize;
        public short dmDriverExtra;
        public int dmFields;
        public int dmPositionX;
        public int dmPositionY;
        public int dmDisplayOrientation;
        public int dmDisplayFixedOutput;
        public short dmColor;
        public short dmDuplex;
        public short dmYResolution;
        public short dmTTOption;
        public short dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string dmFormName;
        public short dmLogPixels;
        public short dmBitsPerPel;
        public int dmPelsWidth;
        public int dmPelsHeight;
        public int dmDisplayFlags;
        public int dmDisplayFrequency;
        public int dmICMMethod;
        public int dmICMIntent;
        public int dmMediaType;
        public int dmDitherType;
        public int dmReserved1;
        public int dmReserved2;
        public int dmPanningWidth;
        public int dmPanningHeight;
    }

    [DllImport("user32.dll")]
    public static extern bool EnumDisplaySettings(string deviceName, int modeNum, ref DEVMODE devMode);

    public const int ENUM_CURRENT_SETTINGS = -1;

    // Independent maxima for the same display; never combine modes from other monitors.
    public static int[] GetMaximumMode(string deviceName) {
        int width = 0, height = 0, frequency = 0;
        long pixels = 0;
        for (int index = 0; ; index++) {
            DEVMODE mode = new DEVMODE();
            mode.dmSize = (short)Marshal.SizeOf(typeof(DEVMODE));
            if (!EnumDisplaySettings(deviceName, index, ref mode)) break;
            long area = (long)mode.dmPelsWidth * mode.dmPelsHeight;
            if (mode.dmPelsWidth > 0 && mode.dmPelsHeight > 0 &&
                (area > pixels || (area == pixels && mode.dmPelsWidth > width))) {
                pixels = area; width = mode.dmPelsWidth; height = mode.dmPelsHeight;
            }
            if (mode.dmDisplayFrequency > 1 && mode.dmDisplayFrequency > frequency)
                frequency = mode.dmDisplayFrequency;
        }
        return new int[] { width, height, frequency };
    }

    public static int GetMaxRefreshRate(string deviceName) {
        if (string.IsNullOrEmpty(deviceName)) {
            deviceName = null;
        }

        int maxHz = 0;
        DEVMODE cur = new DEVMODE();
        cur.dmSize = (short)Marshal.SizeOf(typeof(DEVMODE));
        
        if (EnumDisplaySettings(deviceName, ENUM_CURRENT_SETTINGS, ref cur)) {
            if (cur.dmDisplayFrequency >= 24 && cur.dmDisplayFrequency <= 540) {
                maxHz = cur.dmDisplayFrequency;
            }
        }

        DEVMODE dm = new DEVMODE();
        short devModeSize = (short)Marshal.SizeOf(typeof(DEVMODE));
        int modeIndex = 0;
        int maxAtCurrentRes = 0;
        int maxAnyRes = 0;

        while (true) {
            dm.dmSize = devModeSize;
            if (!EnumDisplaySettings(deviceName, modeIndex++, ref dm)) {
                break;
            }

            int freq = dm.dmDisplayFrequency;
            if (freq >= 24 && freq <= 540) {
                if (freq > maxAnyRes) {
                    maxAnyRes = freq;
                }
                if (cur.dmPelsWidth > 0 && cur.dmPelsHeight > 0 &&
                    dm.dmPelsWidth == cur.dmPelsWidth && dm.dmPelsHeight == cur.dmPelsHeight) {
                    if (freq > maxAtCurrentRes) {
                        maxAtCurrentRes = freq;
                    }
                }
            }
        }

        int candidate = Math.Max(maxAtCurrentRes, maxAnyRes);
        if (candidate > maxHz) {
            maxHz = candidate;
        }

        return maxHz;
    }

    public static int GetCurrentRefreshRate() {
        DEVMODE dm = new DEVMODE();
        dm.dmSize = (short)Marshal.SizeOf(typeof(DEVMODE));
        if (EnumDisplaySettings(null, ENUM_CURRENT_SETTINGS, ref dm)) {
            return dm.dmDisplayFrequency;
        }
        return 0;
    }
}
"@ -ErrorAction SilentlyContinue

# --- HOI WINDOWS QUA DXCORE: CARD NAO LA TICH HOP (iGPU), CARD NAO LA CARD ROI ---
# AMD dung chung ma hang va thuong hieu Radeon cho ca iGPU lan card roi, nen doan theo ten rat de sai
# (VD: iGPU cua Ryzen 7 2700U ten la "Radeon RX Vega 10 Graphics"). DXCore tra loi truc tiep qua
# thuoc tinh IsIntegrated cua driver, kem bo nho rieng (DedicatedAdapterMemory) doc truc tiep.
# Neu Windows khong co dxcore.dll, lop nay tra ve danh sach rong va script dung cach doan theo ten.
Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

public class DxCoreAdapterInfo {
    public uint VendorId;
    public uint DeviceId;
    public bool IsHardware;
    public bool IsIntegrated;
    public ulong DedicatedMemory;
    public string Description;
}

public static class DxCoreHelper {
    [DllImport("dxcore.dll", ExactSpelling = true)]
    static extern int DXCoreCreateAdapterFactory(ref Guid riid, out IntPtr ppvFactory);

    // Thu tu ham trong bang vtable lay tu dxcore_interface.h (DirectX-Headers cua Microsoft).
    // Cac ham template trong header khong nam trong vtable nen khong tinh.
    // IDXCoreAdapterFactory: [3] CreateAdapterList
    // IDXCoreAdapterList   : [3] GetAdapter, [4] GetAdapterCount
    // IDXCoreAdapter       : [5] IsPropertySupported, [6] GetProperty, [7] GetPropertySize
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    delegate int CreateAdapterListFn(IntPtr self, uint numAttributes, ref Guid filterAttributes, ref Guid riid, out IntPtr ppv);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    delegate int GetAdapterFn(IntPtr self, uint index, ref Guid riid, out IntPtr ppv);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    delegate uint GetAdapterCountFn(IntPtr self);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    [return: MarshalAs(UnmanagedType.U1)]
    delegate bool IsPropertySupportedFn(IntPtr self, uint property);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    delegate int GetPropertyFn(IntPtr self, uint property, UIntPtr bufferSize, IntPtr buffer);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    delegate int GetPropertySizeFn(IntPtr self, uint property, out UIntPtr bufferSize);

    // DXCoreAdapterProperty
    const uint PropInstanceLuid = 0;
    const uint PropDriverDescription = 2;
    const uint PropHardwareID = 3;
    const uint PropDedicatedAdapterMemory = 7;
    const uint PropIsHardware = 11;
    const uint PropIsIntegrated = 12;

    static T Fn<T>(IntPtr obj, int slot) where T : class {
        IntPtr vtbl = Marshal.ReadIntPtr(obj);
        IntPtr p = Marshal.ReadIntPtr(vtbl, slot * IntPtr.Size);
        return (T)(object)Marshal.GetDelegateForFunctionPointer(p, typeof(T));
    }

    static byte[] ReadProperty(IntPtr adapter, uint property) {
        if (!Fn<IsPropertySupportedFn>(adapter, 5)(adapter, property)) return null;
        UIntPtr size;
        if (Fn<GetPropertySizeFn>(adapter, 7)(adapter, property, out size) < 0) return null;
        ulong n = size.ToUInt64();
        if (n == 0 || n > 4096) return null;
        IntPtr buf = Marshal.AllocHGlobal((int)n);
        try {
            if (Fn<GetPropertyFn>(adapter, 6)(adapter, property, size, buf) < 0) return null;
            byte[] data = new byte[n];
            Marshal.Copy(buf, data, 0, (int)n);
            return data;
        } finally {
            Marshal.FreeHGlobal(buf);
        }
    }

    public static DxCoreAdapterInfo[] GetAdapters() {
        List<DxCoreAdapterInfo> result = new List<DxCoreAdapterInfo>();
        HashSet<ulong> seenLuids = new HashSet<ulong>();
        IntPtr factory = IntPtr.Zero;
        Guid iidFactory = new Guid("78ee5945-c36e-4b13-a669-005dd11c0f06");
        Guid iidList    = new Guid("526c7776-40e9-459b-b711-f32ad76dfc28");
        Guid iidAdapter = new Guid("f0db4c7f-fe5a-42a2-bd62-f2a6cf6fc83e");

        // Quet ca 3 bo loc roi gop lai. Tren Windows 10 doi 2004, bo loc D3D11 chi tra ve
        // trinh dieu khien phan mem, chi cac bo loc D3D12 moi liet ke duoc card that.
        string[] attributes = new string[] {
            "0c9ece4d-2f6e-4f01-8c96-e89e331b47b1",   // D3D12_GRAPHICS
            "248e2800-a793-4724-abaa-23a6de1be090",   // D3D12_CORE_COMPUTE
            "8c47866b-7583-450d-f0f0-6bada895af4b"    // D3D11_GRAPHICS
        };

        try {
            if (DXCoreCreateAdapterFactory(ref iidFactory, out factory) < 0 || factory == IntPtr.Zero) return result.ToArray();
            CreateAdapterListFn createList = Fn<CreateAdapterListFn>(factory, 3);

            foreach (string attrText in attributes) {
                IntPtr list = IntPtr.Zero;
                Guid attr = new Guid(attrText);
                try {
                    if (createList(factory, 1, ref attr, ref iidList, out list) < 0 || list == IntPtr.Zero) continue;
                    uint count = Fn<GetAdapterCountFn>(list, 4)(list);
                    GetAdapterFn getAdapter = Fn<GetAdapterFn>(list, 3);
                    for (uint i = 0; i < count; i++) {
                        IntPtr adapter;
                        if (getAdapter(list, i, ref iidAdapter, out adapter) < 0 || adapter == IntPtr.Zero) continue;
                        try {
                            byte[] luid = ReadProperty(adapter, PropInstanceLuid);
                            if (luid != null && luid.Length >= 8) {
                                if (!seenLuids.Add(BitConverter.ToUInt64(luid, 0))) continue;   // da co tu bo loc truoc
                            }
                            result.Add(ReadAdapter(adapter));
                        } finally {
                            Marshal.Release(adapter);
                        }
                    }
                } catch {
                    // Bo loc nay loi thi bo qua, van dung ket qua cua cac bo loc khac
                } finally {
                    if (list != IntPtr.Zero) Marshal.Release(list);
                }
            }
        } catch {
            // dxcore.dll khong co (Windows cu) hoac loi COM: tra ve danh sach rong de script dung cach du phong
        } finally {
            if (factory != IntPtr.Zero) Marshal.Release(factory);
        }
        return result.ToArray();
    }

    static DxCoreAdapterInfo ReadAdapter(IntPtr adapter) {
                    DxCoreAdapterInfo info = new DxCoreAdapterInfo();
                    byte[] b;
                    b = ReadProperty(adapter, PropIsHardware);             info.IsHardware = (b != null && b[0] != 0);
                    b = ReadProperty(adapter, PropIsIntegrated);           info.IsIntegrated = (b != null && b[0] != 0);
                    b = ReadProperty(adapter, PropDedicatedAdapterMemory); if (b != null && b.Length >= 8) info.DedicatedMemory = BitConverter.ToUInt64(b, 0);
                    b = ReadProperty(adapter, PropHardwareID);
                    if (b != null && b.Length >= 8) {
                        info.VendorId = BitConverter.ToUInt32(b, 0);
                        info.DeviceId = BitConverter.ToUInt32(b, 4);
                    }
                    b = ReadProperty(adapter, PropDriverDescription);
                    if (b != null) {
                        int len = Array.IndexOf(b, (byte)0);
                        if (len < 0) len = b.Length;
                        info.Description = Encoding.UTF8.GetString(b, 0, len).Trim();
                    }
                    return info;
    }
}
'@ -ErrorAction SilentlyContinue
# --- HÀM PHỤ TRỢ XỬ LÝ RAM HÀN CHẾT & JEDEC MANUFACTURER (ĐA NĂNG & MỞ RỘNG TẤT CẢ CÁC HÃNG) ---
# Giai ma ma dinh danh nha san xuat theo chuan JEDEC JEP106.
# Ma trong SPD gom 2 byte: byte 1 la so lan noi tiep (xac dinh bank), byte 2 la ma hang.
# Ca hai byte deu co the mang bit chan le o bit 7 nen phai che bit nay truoc khi tra cuu.
# Cach nay giai ma duoc MOI hang trong bang JEP106, thay vi liet ke cung tung ma mot.
function Resolve-JedecId($id) {
    if (-not $id) { return "" }

    $c = ([string]$id).Trim().ToUpper() -replace '^0X', ''
    if ($c -notmatch '^[0-9A-F]{4,}$') { return "" }

    $bankByte = [Convert]::ToInt32($c.Substring(0, 2), 16)
    $codeByte = [Convert]::ToInt32($c.Substring(2, 2), 16)

    $bank = ($bankByte -band 0x7F) + 1   # so lan noi tiep + 1 = so hieu bank
    $code = $codeByte -band 0x7F         # bo bit chan le, lay 7 bit ma hang
    if ($code -eq 0 -or $code -eq 0x7F) { return "" }

    # Khoa tra cuu: 2 chu so THAP PHAN cua bank + 2 chu so HEX cua ma hang
    $key = "{0:D2}{1:X2}" -f $bank, $code

    $vendors = @{
        # --- Bank 1: cac hang san xuat chip DRAM goc ---
        "0101" = "AMD";             "0107" = "Hitachi";        "0109" = "Intel"
        "0118" = "Kioxia";          "011C" = "Mitsubishi";     "012C" = "Micron"
        "012D" = "SK Hynix";        "0140" = "ProMos";         "0141" = "Infineon"
        "0145" = "Western Digital"; "014E" = "Samsung";        "0152" = "Alliance Memory"
        "0155" = "ISSI";            "015A" = "Winbond"

        # --- Bank 2 ---
        "0214" = "SMART Modular";   "0218" = "Kingston";       "021D" = "ISSI"
        "0229" = "Vanguard";        "0232" = "Mushkin";        "023A" = "PNY"
        "0240" = "Viking";          "0245" = "Micron";         "024F" = "Transcend"
        "0261" = "Wintec";          "027A" = "Apacer"

        # --- Bank 3 ---
        "031E" = "Corsair";         "0335" = "SpecTek";        "037E" = "Elpida"

        # --- Bank 4 ---
        "040B" = "Nanya";           "0414" = "Mushkin";        "0416" = "Netlist"
        "0419" = "Centon";          "0425" = "Kingmax";        "045A" = "Swissbit"

        # --- Bank 5 ---
        "051E" = "Intelligent Memory"; "0530" = "OCZ";         "0543" = "Ramaxel"
        "0548" = "Powerchip";       "054B" = "ADATA";          "054D" = "G.Skill"
        "0556" = "Chaintech";       "0562" = "Goldenmars";     "056F" = "TeamGroup"
        "0571" = "Toshiba"

        # --- Bank 6 ---
        "0602" = "Patriot";         "061B" = "Crucial";        "061D" = "Rambus"
        "063A" = "Virtium";         "0651" = "Qimonda";        "0677" = "Avant"
        "0678" = "Fidelix"

        # --- Bank 7 ---
        "070A" = "Bright Micron";   "0726" = "Goldenmars";     "0734" = "Super Talent"
        "075D" = "GOODRAM";         "075E" = "Lexar";          "0763" = "ATP"
        "076B" = "TwinMOS";         "076D" = "V-Color";        "0771" = "InnoDisk"

        # --- Bank 8 ---
        "0812" = "HT Micron";       "081D" = "Greenliant";     "0825" = "Ramos"
        "0846" = "Gloway";          "0865" = "Dosilicon";      "086A" = "Transcend"
        "086F" = "Zentel"

        # --- Bank 9 ---
        "0912" = "Galax";           "0913" = "Gloway";         "0918" = "KLEVV"
        "091A" = "UniIC";           "091B" = "YMTC";           "0942" = "Seagate"
        "0948" = "Lenovo";          "0955" = "Etron";          "0971" = "Asgard"
        "0975" = "JUHOR"

        # --- Bank 10 ---
        "1022" = "Maxsun";          "1025" = "Phison";         "102D" = "Neo Forza"
        "104D" = "Longsys";         "1051" = "Antec";          "1061" = "Recadata"
        "1068" = "Kimtigo";         "106C" = "Colorful";       "1077" = "Netac"

        # --- Bank 11 ---
        "1102" = "KingSpec";        "1111" = "CXMT";           "1131" = "Biwin"
        "1142" = "Thermaltake";     "1156" = "Longsys";        "116B" = "Acer"
        "1176" = "Lexar";           "117D" = "JUHOR"

        # --- Bank 12 ---
        "1212" = "Kingbank";        "122C" = "Hikstorage";     "122E" = "aigo"
        "1245" = "WODPOSIT";        "1256" = "HOSIN";          "125E" = "HANA Micron"

        # --- Bank 13 ---
        "1303" = "JHICC";           "132B" = "Biwin";          "1345" = "aigo"
        "137C" = "Hikstorage";      "137D" = "Dell"

        # --- Bank 14 tro len ---
        "1423" = "Union Memory";    "1537" = "Yangtze MasonSemi"
        "1601" = "CXMT";            "163B" = "Powerchip";      "170F" = "KLEVV"
    }

    if ($vendors.ContainsKey($key)) { return $vendors[$key] }
    return ""
}

function Resolve-RamManufacturer($rawMfg, $partNumber = "") {
    $found = ""
    $m = if ($rawMfg) { $rawMfg.Trim() } else { "" }

    # 1. Định dạng 12 ký tự hex: [Module 4 ký tự][4 ký tự 0000][DRAM Chip 4 ký tự]
    # Ví dụ: 0198000080AD => Ưu tiên thương hiệu Module (Kingston), nếu không có mới lấy tên Chip
    if ($m -match "^([0-9A-Fa-f]{4})[0-9A-Fa-f]{4}([0-9A-Fa-f]{4})$") {
        $mod = Resolve-JedecId $matches[1]
        $dram = Resolve-JedecId $matches[2]
        if ($mod) { $found = $mod }
        elseif ($dram) { $found = $dram }
    }

    # 2. Định dạng 4 ký tự hoặc tiền tố 0x (VD: 0x802C, 0x80AD, 0x0198...)
    if (-not $found) {
        $clean = $m -replace '^0x', ''
        $single = Resolve-JedecId $clean
        if ($single) { $found = $single }
    }

    # 3. Chuoi hex dai bat dau bang ma JEDEC: bo giai ma da tu lay 4 ky tu dau nen khong can xu ly rieng

    # 4. Nếu là chuỗi ký tự tên hãng chuẩn (và không phải hex rác hay Unknown)
    if (-not $found -and $m -and ($m -notmatch "^(0x0000|Unknown|None|00000000|[0-9A-Fa-f]{8,16})$")) {
        $found = $m
    }

    # 5. Cơ chế dự phòng thông minh: Tự nhận diện hãng qua mã Part Number (SPD)
    if (-not $found -and $partNumber) {
        $pn = $partNumber.Trim().ToUpper()
        if ($pn -match "^(KHX|KVR|KF|KCP|KSM|KTD|KTL|KTH|KCS|CBD|9905)") { $found = "Kingston" }
        elseif ($pn -match "^(CM[KWDSTHUGNP]|VS[0-9])") { $found = "Corsair" }
        elseif ($pn -match "^(F[2345]-)") { $found = "G.Skill" }
        elseif ($pn -match "^(CT|BL|BLS|BLT|CB[0-9])") { $found = "Crucial" }
        elseif ($pn -match "^(M[0-9]{3}[A-Z]|K4[AFUB])") { $found = "Samsung" }
        elseif ($pn -match "^(HM[ACPT]|H5[ATC]|H9[HJ])") { $found = "SK Hynix" }
        elseif ($pn -match "^(MTA|MTC|MT[0-9]{2}|[0-9]AT[A-Z])") { $found = "Micron" }
        elseif ($pn -match "^(AD[45]|AX[45]|AM[0-9])") { $found = "ADATA" }
        elseif ($pn -match "^(TED[45]|TF[0-9]|TLZ|TLW|FF[0-9]|T[CF]RD)") { $found = "TeamGroup" }
        elseif ($pn -match "^(LD[45]|LK[0-9])") { $found = "Lexar" }
        elseif ($pn -match "^(KD[45]|IM[0-9])") { $found = "KLEVV" }
        elseif ($pn -match "^(PSD|PVS|PV[45]|PSP)") { $found = "Patriot" }
        elseif ($pn -match "^(78\.|AU[0-9]|D[VG]4)") { $found = "Apacer" }
        elseif ($pn -match "^(KM|GL[DR])") { $found = "Kingmax" }
        elseif ($pn -match "^(NT[0-9]|BASIC)") { $found = "Netac" }
        elseif ($pn -match "^(CVN|DDR4S|DDR5S)") { $found = "Colorful" }
        elseif ($pn -match "^(SP[0-9])") { $found = "Silicon Power" }
        elseif ($pn -match "^(GP[0-9]|GAM|GVX|G[ABF]4)") { $found = "GeIL" }
        elseif ($pn -match "^(RMT|RMS|RML)") { $found = "Ramaxel" }
        elseif ($pn -match "^(NB[0-9]|NU[0-9])") { $found = "Nanya" }
        elseif ($pn -match "^(W[0-9]{3}|EBE|EBJ)") { $found = "Elpida" }
        elseif ($pn -match "^(TS[0-9])") { $found = "Transcend" }
        elseif ($pn -match "^(GR[0-9]|IR[0-9])") { $found = "GOODRAM" }
        elseif ($pn -match "^(MD[0-9]|KP[0-9])") { $found = "Kingbank" }
        elseif ($pn -match "^(JHT|JH[0-9])") { $found = "JUHOR" }
        elseif ($pn -match "^(A3[0-9]|AS[0-9])") { $found = "Asgard" }
        elseif ($pn -match "^(GLOWAY|TF[0-9]{2})") { $found = "Gloway" }
        elseif ($pn -match "^(CXDM|CXMT)") { $found = "CXMT" }
        elseif ($pn -match "^(TTZ|R[0-9]{3}D)") { $found = "Thermaltake" }
        elseif ($pn -match "^(FS[0-9]|UMIS)") { $found = "Union Memory" }
    }

    return $found
}

# Ma loai bo nho theo dac ta SMBIOS (Type 17, offset 0x12), doi chieu voi ma nguon BIOS chuan EDK2:
#   0x1A DDR4   0x22 DDR5   0x25 MRDIMM (bien the DDR5 may chu)
#   0x1E LPDDR4 0x23 LPDDR5
# SMBIOS KHONG co ma rieng cho LPDDR4X va LPDDR5X. BIOS ghi chung vao LPDDR4 / LPDDR5,
# nen phai dua vao toc do de tach: LPDDR4 toi da 3200 MT/s, LPDDR5 toi da 6400 MT/s theo JEDEC.
# Vuot cac nguong nay thi chi co the la ban X.
# Luu y: ma 0x1F la "Logical non-volatile device" va 0x24 la HBM3, KHONG phai LPDDR4X / LPDDR5X.
function Resolve-DdrType($smbiosType, $speed, $cpuName) {
    $spd = 0
    if ($speed) { [void][int]::TryParse([string]$speed, [ref]$spd) }

    $t = 0
    if ($smbiosType) { [void][int]::TryParse([string]$smbiosType, [ref]$t) }

    switch ($t) {
        0x18 { return "DDR3" }   # giu lai de may cu khong bi ghi nham thanh DDR4
        0x1A { return "DDR4" }
        0x22 { return "DDR5" }
        0x25 { return "DDR5" }
        0x1E { if ($spd -gt 3200) { return "LPDDR4X" } else { return "LPDDR4" } }
        0x23 { if ($spd -gt 6400) { return "LPDDR5X" } else { return "LPDDR5" } }
    }

    # BIOS khong khai bao loai: chi suy theo toc do cho DDR4 / DDR5.
    # Khong doan LPDDR tu toc do vi DDR5-6400 tro len cung ton tai, de nham.
    if ($spd -ge 4800) { return "DDR5" }
    return "DDR4"
}

# --- DOC TRUC TIEP BANG SMBIOS THO TU BIOS (cau truc Type 17 - Memory Device) ---
# Win32_PhysicalMemory cua WMI da doi so hieu Form Factor sang he enum rieng cua CIM,
# trong do KHONG co gia tri "Row of chips" - dau hieu duy nhat cho biet RAM han chet.
# Vi vay phai doc thang bang SMBIOS goc thi moi phan biet duoc ONBOARD va SODIMM.
function Get-SmbiosMemoryDevices {
    $devices = @()
    try {
        $raw = (Get-CimInstance -Namespace root\wmi -ClassName MSSmBios_RawSMBiosTables -ErrorAction Stop).SMBiosData
        if (-not $raw -or $raw.Length -lt 8) { return $devices }

        $i = 0
        while ($i -lt ($raw.Length - 4)) {
            $sType = $raw[$i]
            $sLen  = $raw[$i + 1]
            if ($sLen -lt 4) { break }

            # Vung chuoi nam ngay sau phan du lieu va ket thuc boi hai byte 00 00
            $strStart = $i + $sLen
            $q = $strStart
            while ($q -lt ($raw.Length - 1) -and -not ($raw[$q] -eq 0 -and $raw[$q + 1] -eq 0)) { $q++ }
            $nextStruct = $q + 2

            $strings = @()
            $p = $strStart
            while ($p -lt $q) {
                $sb = New-Object System.Text.StringBuilder
                while ($p -lt $q -and $raw[$p] -ne 0) {
                    [void]$sb.Append([char]$raw[$p])
                    $p++
                }
                $strings += $sb.ToString()
                $p++
            }

            if ($sType -eq 17 -and ($i + $sLen) -le $raw.Length -and $sLen -gt 0x12) {
                $sizeRaw = [BitConverter]::ToUInt16($raw, $i + 0x0C)
                if ($sizeRaw -ne 0) {   # bo qua khe cam trong
                    $locIdx = $raw[$i + 0x10]
                    $locator = if ($locIdx -ge 1 -and $locIdx -le $strings.Count) { $strings[$locIdx - 1].Trim() } else { "" }
                    $devices += [PSCustomObject]@{
                        FormFactor = [int]$raw[$i + 0x0E]
                        MemoryType = [int]$raw[$i + 0x12]
                        Locator    = $locator
                    }
                }
            }

            if ($sType -eq 127) { break }   # Type 127 = ket thuc bang
            if ($nextStruct -le $i) { break }
            $i = $nextStruct
        }
    } catch {}
    return $devices
}

# --- HÀM KIỂM TRA TRẠNG THÁI BẢN QUYỀN WINDOWS (ACTIVE / CHƯA ACTIVE) ---
function Get-WindowsActivationStatus {
    try {
        # Truy vấn trực tiếp ApplicationId của Windows OS (55c92734-d682-4d71-983e-d6ec3f16059f)
        $lic = Get-CimInstance SoftwareLicensingProduct -Filter "ApplicationId = '55c92734-d682-4d71-983e-d6ec3f16059f' and PartialProductKey is not null" -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($lic) {
            return ($lic.LicenseStatus -eq 1)
        }
        # Phương án dự phòng qua slmgr nếu không tìm thấy WMI
        $slmgr = cscript //nologo "$env:SystemRoot\system32\slmgr.vbs" /dli 2>&1
        if ($slmgr -match "License Status:\s*Licensed") {
            return $true
        }
        return $false
    }
    catch {
        return $false
    }
}

# --- HÀM LẤY CHÍNH XÁC VRAM CARD RỜI TỪ REGISTRY TRÁNH LỖI 32-BIT OVERFLOW ---
function Get-GpuVram($gpuName, $adapterRam) {
    try {
        $regPath = "HKLM:\SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}"
        $subKeys = Get-ChildItem $regPath -ErrorAction SilentlyContinue
        foreach ($k in $subKeys) {
            $p = Get-ItemProperty $k.PSPath -ErrorAction SilentlyContinue
            if ($p.DriverDesc -and $p.DriverDesc.Trim() -eq $gpuName.Trim()) {
                if ($p.'HardwareInformation.qwMemorySize') {
                    $bytes = [int64]$p.'HardwareInformation.qwMemorySize'
                    if ($bytes -gt 0) {
                        $gb = [math]::Round($bytes / 1GB, 0)
                        if ($gb -gt 0) { return "$($gb)GB" }
                    }
                }
                if ($p.'HardwareInformation.MemorySize') {
                    $mSize = $p.'HardwareInformation.MemorySize'
                    if ($mSize -is [byte[]] -and $mSize.Length -ge 4) {
                        $bytes = [System.BitConverter]::ToUInt32($mSize, 0)
                        if ($bytes -gt 0) {
                            $gb = [math]::Round($bytes / 1GB, 0)
                            if ($gb -gt 0) { return "$($gb)GB" }
                        }
                    }
                }
            }
        }
    } catch {}

    if ($adapterRam -and $adapterRam -gt 0) {
        $gb = [math]::Round($adapterRam / 1GB, 0)
        if ($gb -gt 0) { return "$($gb)GB" }
    }
    return ""
}

# --- BO NHO RIENG CUA GPU THEO DIRECTX (DedicatedVideoMemory) ---
# Day chinh la con so Windows hien thi o dxdiag va Display adapter properties.
# Voi iGPU, AdapterRAM cua WMI va registry driver thuong bao sai (VD: Intel luon ghi 1024MB),
# con DirectX tra ve dung phan bo nho BIOS cap rieng cho do hoa (VD: 128MB, 256MB).
# Neu co nhieu ban ghi trung ten (sau khi doi driver / BIOS), lay ban ghi LastSeen moi nhat.
function Get-DxDedicatedMemory($gpuName) {
    if (-not $gpuName) { return "" }
    try {
        $best = $null
        $bestSeen = [int64]-1
        foreach ($k in (Get-ChildItem "HKLM:\SOFTWARE\Microsoft\DirectX" -ErrorAction SilentlyContinue)) {
            $p = Get-ItemProperty $k.PSPath -ErrorAction SilentlyContinue
            if (-not $p -or -not $p.Description) { continue }
            if ($p.Description.Trim() -ne $gpuName.Trim()) { continue }
            if ($p.SoftwareAdapter -eq 1) { continue }
            $seen = if ($p.LastSeen) { [int64]$p.LastSeen } else { [int64]0 }
            if ($seen -gt $bestSeen) { $bestSeen = $seen; $best = $p }
        }
        if ($best -and $best.DedicatedVideoMemory) {
            return (Format-GpuMemory ([int64]$best.DedicatedVideoMemory))
        }
    } catch {}
    return ""
}

# Doi so byte bo nho do hoa sang chuoi hien thi: duoi 1024MB ghi MB, tu 1024MB tro len ghi GB
function Format-GpuMemory($bytes) {
    try { $b = [double]$bytes } catch { return "" }
    if ($b -le 0) { return "" }
    $mb = [math]::Round($b / 1MB, 0)
    if ($mb -ge 1024) {
        $gb = [math]::Round($b / 1GB, 1)
        if (($gb % 1) -eq 0) { return "$([int]$gb)GB" } else { return "$($gb)GB" }
    }
    return "$($mb)MB"
}

# Danh sach card do hoa theo DXCore, tra ve bang tra cuu theo khoa "VVVV:DDDD" (ma hang : ma thiet bi PCI).
# Chi lay card phan cung, bo qua "Microsoft Basic Render Driver".
function Get-DxCoreGpuInfo {
    $map = @{}
    try {
        if (-not ('DxCoreHelper' -as [type])) { return $map }
        foreach ($a in [DxCoreHelper]::GetAdapters()) {
            if (-not $a.IsHardware -or $a.VendorId -eq 0) { continue }
            $key = "{0:X4}:{1:X4}" -f $a.VendorId, $a.DeviceId
            if (-not $map.ContainsKey($key)) {
                $map[$key] = [PSCustomObject]@{
                    IsIntegrated    = [bool]$a.IsIntegrated
                    DedicatedMemory = [uint64]$a.DedicatedMemory
                    Description     = [string]$a.Description
                }
            }
        }
    } catch {}
    return $map
}

# Cach DU PHONG khi DXCore khong tra loi duoc (Windows cu, card doi cu khong ho tro D3D12).
# Phan biet theo ma hang PCI truoc, roi moi xet mau ten.
function Test-GpuDiscreteByName($name, $pnp) {
    $n = [string]$name
    $p = [string]$pnp
    $s = ($n -replace '\(TM\)|\(R\)|™|®', ' ') -replace '\s+', ' '

    # NVIDIA: moi card deu la card roi
    if ($p -match 'VEN_10DE' -or $s -match 'NVIDIA|GeForce|Quadro|Tesla|TITAN|\bRTX\b|\bGTX\b') { return $true }

    # Intel: chi Arc co so model (A380, A770M, B580...) va Iris Xe MAX la card roi.
    # "Intel Arc Graphics" hay "Arc 140V" tren Core Ultra la card TICH HOP.
    if ($p -match 'VEN_8086' -or $s -match '^\s*Intel') {
        return ($s -match '\bArc [AB]\d{2,3}' -or $s -match 'Iris.*Xe MAX')
    }

    # AMD: cac mau ten cua iGPU, con lai la card roi
    if ($p -match 'VEN_1002' -or $s -match '\bAMD\b|Radeon|\bATI\b') {
        if ($s -match 'Radeon (RX )?Vega \d+ Graphics') { return $false }   # Ryzen 2000 - 5000 (VD: RX Vega 10 cua 2700U)
        if ($s -match 'Radeon R[2-7] Graphics')         { return $false }   # APU dong A / E
        if ($s -match 'Radeon Graphics\s*$')            { return $false }   # ten chung tu Ryzen 4000 tro len
        if ($s -match 'Radeon \d{3}M\b')                { return $false }   # 610M, 680M, 780M, 880M, 890M...
        if ($s -match 'Radeon \d{4}S\b')                { return $false }   # 8050S, 8060S (Ryzen AI Max)
        if ($s -match 'Radeon (HD )?\d{4}[GD]\b')       { return $false }   # HD 7660D, HD 8610G (APU doi cu)
        return $true
    }

    return $false
}

# --- HÀM TRUY VẤN CHÍNH XÁC CHO WINDOWS 10 & 11 ---
# $useCache = $true: che do tu dong lam moi, dung lai ket qua cua cac phep do nang va khong doi
#   (ban quyen Windows, nvidia-smi, danh sach card DXCore). Cac so lieu thay doi nhu dung luong
#   o dia, man hinh, RAM van doc moi.
# $useCache = $false: quet lai toan bo (luc mo ung dung va khi bam F5).
function Get-SystemData($onProgress = $null, $useCache = $false) {
    if (-not ($global:InfoCache -is [hashtable])) { $global:InfoCache = @{} }
    try {
        # 1. OS & Trạng thái bản quyền
        if ($onProgress) { & $onProgress "[1/5] Đang kiểm tra hệ điều hành & bản quyền Windows..." 20 }
        $regNT = Get-ItemProperty "HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion" -ErrorAction SilentlyContinue
        $buildNumber = if ($regNT.CurrentBuildNumber) { [int]$regNT.CurrentBuildNumber } else { 0 }
        $edition = switch -Regex ($regNT.EditionID) {
            "Pro"        { "Pro" }
            "Core"       { "Home" }
            "Enterprise" { "Enterprise" }
            "Education"  { "Education" }
            default      { if ($regNT.EditionID) { $regNT.EditionID } else { "Pro" } }
        }
        $osDisplay = if ($buildNumber -ge 22000) { "Windows 11 $edition" } else { "Windows 10 $edition" }
        # Kiem tra ban quyen ton thoi gian (phai truy van dich vu cap phep) nen chi chay lai khi lam moi thu cong
        if ($useCache -and $global:InfoCache.ContainsKey('Activation')) {
            $isActivated = $global:InfoCache['Activation']
        } else {
            $isActivated = Get-WindowsActivationStatus
            $global:InfoCache['Activation'] = $isActivated
        }

        # 2. Hostname, Model, Serial, CPU
        if ($onProgress) { & $onProgress "[2/5] Đang kiểm tra vi xử lý (CPU) & bo mạch..." 40; Start-Sleep -Milliseconds 70 }
        # 2. Hostname
        $hostDisplay = try { [System.Net.Dns]::GetHostName() } catch { $env:COMPUTERNAME }
        if (-not $hostDisplay) { $hostDisplay = $env:COMPUTERNAME }

        # 3. Model
        $csObj = Get-CimInstance Win32_ComputerSystem -ErrorAction SilentlyContinue
        $bbObj = Get-CimInstance Win32_BaseBoard -ErrorAction SilentlyContinue
        $modelDisplay = if ($csObj.Model -and $csObj.Model -notmatch "System Product Name|To be filled|Default string") {
            $csObj.Model.Trim()
        } elseif ($bbObj.Product) {
            $bbObj.Product.Trim()
        } else {
            "PC"
        }

        # 4. Serial
        $biosObj = Get-CimInstance Win32_BIOS -ErrorAction SilentlyContinue
        $serialDisplay = if ($biosObj.SerialNumber -and $biosObj.SerialNumber -notmatch "To be filled|Default string|System Serial|None") {
            $biosObj.SerialNumber.Trim()
        } elseif ($bbObj.SerialNumber -and $bbObj.SerialNumber -notmatch "To be filled|Default string") {
            $bbObj.SerialNumber.Trim()
        } else {
            "N/A"
        }

        # 5. CPU
        $cpuReg = Get-ItemProperty "HKLM:\HARDWARE\DESCRIPTION\System\CentralProcessor\0" -ErrorAction SilentlyContinue
        $cpuDisplay = if ($cpuReg.ProcessorNameString) {
            $cpuReg.ProcessorNameString.Trim() -replace '\s+', ' '
        } else {
            (Get-CimInstance Win32_Processor -ErrorAction SilentlyContinue | Select-Object -First 1).Name.Trim() -replace '\s+', ' '
        }

        # 6. RAM (Trình bày dạng cây phân nhánh khoa học HWiNFO / Speccy)
        if ($onProgress) { & $onProgress "[3/5] Đang quét cấu hình & module bộ nhớ RAM..." 60; Start-Sleep -Milliseconds 70 }
        $memModules = @(Get-CimInstance Win32_PhysicalMemory -ErrorAction SilentlyContinue)
        $totalRAMGB = if ($memModules.Count -gt 0) {
            [math]::Round(($memModules | Measure-Object -Property Capacity -Sum).Sum / 1GB, 0)
        } else {
            $osCim = Get-CimInstance Win32_OperatingSystem -ErrorAction SilentlyContinue
            [math]::Round($osCim.TotalVisibleMemorySize / 1MB, 0)
        }

        $chassisTypes = (Get-CimInstance Win32_SystemEnclosure -ErrorAction SilentlyContinue).ChassisTypes
        $isLaptop = $chassisTypes -contains 9 -or $chassisTypes -contains 10 -or $chassisTypes -contains 14 -or $chassisTypes -contains 30
        
        $modelFull = "$($csObj.Model) $($csObj.SystemFamily) $($csObj.SystemSKUNumber)"
        $isKnownSolderedModel = $modelFull -match "T490s|T14s|X1 Carbon|X1 Yoga|X1 Nano|X13|X390|X280|Z13|Z16|XPS 13|Surface|ZenBook|LG Gram|Spectre|Dragonfly|Yoga Slim|Swift 3|Swift 5|Swift 7|MacBook"

        $speed = 2667
        $ddrType = "DDR4"
        $caps = @()
        $solderedFlags = @()
        $mfgList = @()
        $cfgSpeeds = @()     # ConfiguredClockSpeed: toc do RAM DANG CHAY thuc te
        $ratedSpeeds = @()   # Speed: toc do ghi tren nhan SPD (chi dung du phong)
        $formFactors = @()   # Ket luan SODIMM / DIMM / ONBOARD cho tung thanh
        $ramItems = @()

        # Doc bang SMBIOS goc tu BIOS de lay Form Factor chuan xac
        $smbiosDevs = @(Get-SmbiosMemoryDevices)

        for ($i = 0; $i -lt $memModules.Count; $i++) {
            $m = $memModules[$i]
            $mGB = [math]::Round($m.Capacity / 1GB, 0)
            $caps += $mGB

            if ($m.ConfiguredClockSpeed -and [int]$m.ConfiguredClockSpeed -gt 0) {
                $cfgSpeeds += [int]$m.ConfiguredClockSpeed
            }
            if ($m.Speed -and [int]$m.Speed -gt 0) {
                $ratedSpeeds += [int]$m.Speed
            }

            # Khop thanh RAM cua WMI voi cau truc SMBIOS tuong ung theo ten vi tri, neu khong duoc thi theo thu tu
            $dev = $null
            if ($m.DeviceLocator) {
                $dev = $smbiosDevs | Where-Object { $_.Locator -and $_.Locator -eq $m.DeviceLocator.Trim() } | Select-Object -First 1
            }
            if (-not $dev -and $i -lt $smbiosDevs.Count) { $dev = $smbiosDevs[$i] }

            # Loai RAM: uu tien ma cua WMI, neu trong thi lay thang ma trong bang SMBIOS cua BIOS
            $memTypeCode = if ($m.SMBIOSMemoryType -and [int]$m.SMBIOSMemoryType -gt 0) { [int]$m.SMBIOSMemoryType }
                           elseif ($dev) { $dev.MemoryType }
                           else { 0 }
            $ddrType = Resolve-DdrType $memTypeCode $m.Speed $cpuDisplay

            # --- XAC DINH CHUAN CHAN CAM: uu tien tuyet doi bang SMBIOS goc tu BIOS ---

            $biosFF = if ($dev) { $dev.FormFactor } else { 0 }
            # Bang gia tri Form Factor theo dac ta SMBIOS (khac voi he enum cua WMI):
            #   0x05 Chip, 0x0A TSOP, 0x0B Row of chips, 0x10 Die  => chip han thang len bo mach
            #   0x0D SODIMM                                        => khe cam laptop
            #   0x09 DIMM, 0x0C RIMM, 0x0F FB-DIMM                 => khe cam may ban
            $biosOnboard = $biosFF -in @(5, 10, 11, 16)
            $biosSodimm  = $biosFF -eq 13
            $biosDimm    = $biosFF -in @(9, 12, 15)

            # LPDDR luon luon han chet, khong ton tai dang khe cam
            $isLpddr = ($ddrType -match "LPDDR") -or (($dev) -and ($dev.MemoryType -in @(27, 28, 29, 30, 35)))

            # Lop du phong cho cac BIOS khai bao sai (VD: ThinkPad T490s ghi SODIMM du RAM han chet)
            $hintSoldered = ($m.DeviceLocator -match "Onboard|Embedded|Soldered") -or
                            ($m.BankLabel -match "Onboard|Embedded|Soldered") -or
                            ($m.PartNumber -match "Soldered|Embedded|Onboard|^4ATS") -or
                            ($isKnownSolderedModel) -or
                            ($isLaptop -and ($m.SerialNumber -match "^(0+|0x0+|None|N/A|Unknown|)$")) -or
                            (-not $m.PartNumber -and -not $m.Manufacturer)

            $thisFF = if ($biosOnboard) { "ONBOARD" }
                      elseif ($isLpddr) { "ONBOARD" }
                      elseif ($hintSoldered) { "ONBOARD" }
                      elseif ($biosSodimm) { "SODIMM" }
                      elseif ($biosDimm) { "DIMM" }
                      elseif ($m.FormFactor -eq 12 -or $isLaptop) { "SODIMM" }
                      else { "DIMM" }

            $formFactors += $thisFF
            $solderedFlags += ($thisFF -eq "ONBOARD")

            $mfg = Resolve-RamManufacturer $m.Manufacturer $m.PartNumber
            $mfgList += $mfg
            $moduleSpeed = if ($m.ConfiguredClockSpeed -gt 0) { $m.ConfiguredClockSpeed } elseif ($m.Speed -gt 0) { $m.Speed } else { 0 }
            $ramItems += [PSCustomObject]@{
                Slot = if ($m.DeviceLocator) { $m.DeviceLocator.Trim() } elseif ($thisFF -eq 'ONBOARD') { 'Onboard' } else { '' }
                Capacity = if ($m.Capacity -gt 0) { "$mGB GB" } else { '' }
                Type = if ($memTypeCode -in @(0x18, 0x1A, 0x22, 0x25, 0x1E, 0x23)) { $ddrType } else { '' }
                Speed = if ($moduleSpeed -gt 0) { "$moduleSpeed MT/s" } else { '' }
                Manufacturer = $mfg
                FormFactor = $thisFF
            }
        }

        # Uu tien toc do RAM dang hoat dong thuc te. Neu BIOS khong khai bao thi moi lay toc do nhan SPD.
        # Lay gia tri nho nhat vi khi lap lan thanh khac toc do, toan he thong chay o muc thap nhat.
        if ($cfgSpeeds.Count -gt 0) {
            $speed = ($cfgSpeeds | Measure-Object -Minimum).Minimum
        } elseif ($ratedSpeeds.Count -gt 0) {
            $speed = ($ratedSpeeds | Measure-Object -Minimum).Minimum
        }

        $hasSoldered = $solderedFlags -contains $true
        $hasSocketed = $solderedFlags -contains $false

        # Chuan chan cam tong the, tong hop tu ket luan cua tung thanh RAM
        $uniqueFF = @($formFactors | Select-Object -Unique)
        $mainFormFactor = if ($uniqueFF.Count -eq 1) {
            $uniqueFF[0]
        } elseif ($uniqueFF.Count -gt 1) {
            # May vua co thanh han vua co khe cam: ghi ro ca hai, dat khe cam truoc
            (@($uniqueFF | Where-Object { $_ -ne "ONBOARD" }) + @($uniqueFF | Where-Object { $_ -eq "ONBOARD" })) -join " + "
        } elseif ($isLaptop) {
            "ONBOARD"
        } else {
            "DIMM"
        }

        # Cấu trúc chuỗi cấu hình: (2x 8GB) hoặc (8GB Onboard + 8GB Slot)
        $configStr = ""
        if ($memModules.Count -eq 0) {
            $hasSoldered = $true
            # WMI khong liet ke duoc thanh nao (hay gap o may LPDDR han chet): lay ma loai tu bang SMBIOS
            $fallbackType = if ($smbiosDevs.Count -gt 0) { $smbiosDevs[0].MemoryType } else { 0 }
            $ddrType = Resolve-DdrType $fallbackType $speed $cpuDisplay
        } elseif ($memModules.Count -eq 1) {
            if (-not $hasSoldered) {
                $configStr = "(1x $($caps[0])GB)"
            }
        } else {
            if ($hasSoldered -and $hasSocketed) {
                $configStr = "($($caps[0])GB Onboard + $($caps[1])GB Slot)"
            } else {
                $allSame = ($caps | Select-Object -Unique).Count -eq 1
                if ($allSame) {
                    $configStr = "($($memModules.Count)x $($caps[0])GB)"
                } else {
                    $configStr = "(" + (($caps | ForEach-Object { "$($_)GB" }) -join " + ") + ")"
                }
            }
        }

        # Dòng 1: Dòng tổng quan cấu hình có đủ kiểu SODIMM / DIMM / ONBOARD
        $cfgPart = if ($configStr) { " $configStr" } else { "" }
        $ramHeader = "$mainFormFactor $($totalRAMGB)GB$cfgPart $ddrType $($speed)MHz"

        # Dòng 2+: Các nhánh cây hiển thị chi tiết từng thanh RAM
        $treeLines = @()
        $branchMid  = "$([char]0x251C)$([char]0x2500)$([char]0x2500) " # ├── 
        $branchLast = "$([char]0x2514)$([char]0x2500)$([char]0x2500) " # └── 

        if ($memModules.Count -eq 0) {
            $treeLines += "$branchLast" + "Onboard: $($totalRAMGB)GB - Soldered Memory"
        } else {
            $slotCounter = 1
            $onboardCounter = 1
            $totalSoldered = ($solderedFlags | Where-Object { $_ -eq $true }).Count

            for ($i = 0; $i -lt $memModules.Count; $i++) {
                $branch = if ($i -eq ($memModules.Count - 1)) { $branchLast } else { $branchMid }
                $isSoldered = $solderedFlags[$i]
                $mGB = $caps[$i]
                $mfg = $mfgList[$i]

                $slotLabel = if ($isSoldered) {
                    if ($totalSoldered -gt 1) { "Onboard $onboardCounter" } else { "Onboard" }
                } else {
                    "Slot $slotCounter"
                }
                if ($isSoldered) { $onboardCounter++ } else { $slotCounter++ }

                $mfgTag = if ($mfg) { " - $mfg" } else { "" }
                $treeLines += "$branch$($slotLabel): $($mGB)GB$mfgTag"
            }
        }

        $ramDisplay = ($ramHeader, ($treeLines -join "`n")) -join "`n"

        # 7. Graphics Card (Rà soát toàn diện hệ thống: iGPU & dGPU kèm VRAM, TGP công suất chuẩn xác)
        if ($onProgress) { & $onProgress "[4/5] Đang nhận diện card đồ họa GPU & VRAM..." 80; Start-Sleep -Milliseconds 70 }
        $rawGpus = @(Get-CimInstance Win32_VideoController -ErrorAction SilentlyContinue)
        $filteredGpus = @($rawGpus | Where-Object {
            $_.Name -and 
            $_.Name -notmatch "Remote Desktop|Citrix|spacedesk|VirtualBox|VMware|IddSampleDriver|Twomon|AirDisplay|Duet" -and
            ($_.PNPDeviceID -like "PCI\*" -or $_.CurrentHorizontalResolution -gt 0 -or $_.AdapterRAM -gt 0)
        })

        # Bổ sung từ Device Manager (PnpDevice) nếu có card dGPU đang ngủ sâu mà WMI chưa báo
        try {
            $pnpDisplays = @(Get-PnpDevice -Class Display -Status OK -ErrorAction SilentlyContinue | Where-Object {
                $_.FriendlyName -and $_.FriendlyName -notmatch "Basic Display|Remote|Citrix|spacedesk|Virtual"
            })
            foreach ($p in $pnpDisplays) {
                $alreadyExists = $filteredGpus | Where-Object { $_.Name.Trim() -eq $p.FriendlyName.Trim() }
                if (-not $alreadyExists) {
                    $filteredGpus += [PSCustomObject]@{
                        Name                        = $p.FriendlyName
                        PNPDeviceID                 = $p.InstanceId
                        AdapterRAM                  = 0
                        CurrentHorizontalResolution = 0
                        CurrentRefreshRate          = 0
                    }
                }
            }
        } catch {}

        # Deduplicate theo tên card
        $uniqueGpus = @()
        $seen = @{}
        foreach ($g in $filteredGpus) {
            $n = $g.Name.Trim()
            if (-not $seen[$n]) {
                $seen[$n] = $true
                $uniqueGpus += $g
            }
        }

        # Truy vấn thông số chuẩn xác (VRAM & Công suất Watt/TGP) từ nvidia-smi nếu có NVIDIA
        # Khi tu dong lam moi, dung lai ket qua cu: VRAM va TGP toi da khong doi, con moi lan goi
        # nvidia-smi se danh thuc card roi dang ngu tren laptop, gay ton pin va quat chay.
        $nvFromCache = $useCache -and $global:InfoCache.ContainsKey('NvDetails')
        $nvDetails = if ($nvFromCache) { $global:InfoCache['NvDetails'] } else { @{} }
        if (-not $nvFromCache) { try {
            $smiPath = $null
            $cmd = Get-Command nvidia-smi -ErrorAction SilentlyContinue
            if ($cmd -and (Test-Path $cmd.Source)) { $smiPath = $cmd.Source }
            if (-not $smiPath) {
                $candidates = @(
                    "$env:SystemRoot\System32\nvidia-smi.exe",
                    "$env:ProgramFiles\NVIDIA Corporation\NVSMI\nvidia-smi.exe"
                )
                foreach ($c in $candidates) {
                    if (Test-Path $c) { $smiPath = $c; break }
                }
            }
            if (-not $smiPath) {
                $ds = "$env:SystemRoot\System32\DriverStore\FileRepository"
                if (Test-Path $ds) {
                    $found = Get-ChildItem -Path $ds -Filter "nvidia-smi.exe" -Recurse -ErrorAction SilentlyContinue | Select-Object -First 1
                    if ($found) { $smiPath = $found.FullName }
                }
            }

            if ($smiPath) {
                # Truy van power.max_limit TRUOC: day la tran cong suat toi da phan cung cho phep (gan TGP nhat).
                # power.limit chi la muc tran dang ap dung tuc thoi, tut xuong khi chay pin / GPU dang ngu.
                $smiOut = & $smiPath --query-gpu=name,memory.total,power.max_limit,power.default_limit,power.limit --format=csv,noheader,nounits 2>$null
                if ($smiOut) {
                    foreach ($line in $smiOut) {
                        $parts = $line -split ',\s*'
                        if ($parts.Count -ge 2) {
                            $nvName = $parts[0].Trim()
                            $nvMemMiB = 0
                            # [void] BAT BUOC: TryParse tra ve Boolean, neu khong chan se lot vao output cua ham
                            [void][double]::TryParse($parts[1].Trim(), [ref]$nvMemMiB)
                            $nvMemGB = if ($nvMemMiB -gt 0) { [math]::Round($nvMemMiB / 1024, 0) } else { 0 }

                            # Quet ca 3 truong cong suat va lay gia tri CAO NHAT, khong dung o gia tri dau tien
                            $nvWatt = 0
                            for ($k = 2; $k -lt $parts.Count; $k++) {
                                $vStr = $parts[$k].Trim()
                                $vNum = 0
                                if ($vStr -and $vStr -ne "[N/A]" -and [double]::TryParse($vStr, [ref]$vNum)) {
                                    if ($vNum -gt $nvWatt) {
                                        $nvWatt = [math]::Round($vNum, 0)
                                    }
                                }
                            }

                            $nvDetails[$nvName] = @{
                                MemGB   = $nvMemGB
                                Wattage = $nvWatt
                            }
                        }
                    }
                }
            }
        } catch {}
        $global:InfoCache['NvDetails'] = $nvDetails
        }

        # Phân loại rõ ràng iGPU (on-board) và dGPU (rời)
        # Nguon chinh: DXCore cua Windows (thuoc tinh IsIntegrated cua driver). Ket qua duoc giu lai
        # trong cac lan tu dong lam moi vi phan cung khong doi, chi quet lai khi lam moi thu cong.
        if ($useCache -and $global:InfoCache.ContainsKey('DxCore')) {
            $dxInfo = $global:InfoCache['DxCore']
        } else {
            $dxInfo = Get-DxCoreGpuInfo
            $global:InfoCache['DxCore'] = $dxInfo
        }

        $igpuList = @()
        $dgpuList = @()

        foreach ($g in $uniqueGpus) {
            $n = $g.Name.Trim()
            $pnp = if ($g.PNPDeviceID) { $g.PNPDeviceID } else { "" }

            # Khop card cua WMI voi DXCore theo ma hang + ma thiet bi PCI, khong theo ten
            $dx = $null
            if ($pnp -match 'VEN_([0-9A-Fa-f]{4}).*?DEV_([0-9A-Fa-f]{4})') {
                $dxKey = "$($matches[1].ToUpper()):$($matches[2].ToUpper())"
                if ($dxInfo.ContainsKey($dxKey)) { $dx = $dxInfo[$dxKey] }
            }
            if (-not $dx) {
                $dx = $dxInfo.Values | Where-Object { $_.Description -and $_.Description.Trim() -eq $n } | Select-Object -First 1
            }

            $isDiscrete = if ($dx) { -not $dx.IsIntegrated } else { Test-GpuDiscreteByName $n $pnp }
            $dxMem = if ($dx) { [uint64]$dx.DedicatedMemory } else { [uint64]0 }

            # Khớp thông số từ nvidia-smi
            $matchedNv = $null
            foreach ($k in $nvDetails.Keys) {
                if ($n -like "*$k*" -or $k -like "*$n*") {
                    $matchedNv = $nvDetails[$k]
                    break
                }
            }

            $vramGB = ""
            $wattage = 0

            if ($matchedNv) {
                if ($matchedNv.MemGB -gt 0) { $vramGB = "$($matchedNv.MemGB)GB" }
                if ($matchedNv.Wattage -gt 0) { $wattage = $matchedNv.Wattage }
            }

            # Nếu chưa có VRAM, đọc từ Registry (chi ap dung cho card roi)
            if (-not $vramGB -and $isDiscrete) {
                $regVram = Get-GpuVram $n $g.AdapterRAM
                if ($regVram) { $vramGB = $regVram }
            }
            # Card roi van chua co VRAM (VD: driver AMD khong ghi registry): lay tu DXCore, lam tron GB
            if (-not $vramGB -and $isDiscrete -and $dxMem -gt 0) {
                $dxGB = [math]::Round($dxMem / 1GB, 0)
                if ($dxGB -gt 0) { $vramGB = "$($dxGB)GB" }
            }

            # Thử tìm Wattage / TGP từ Registry (NVIDIA / AMD) nếu chưa có từ nvidia-smi
            if (-not $wattage -and $isDiscrete) {
                try {
                    $regClass = "HKLM:\SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}"
                    foreach ($sub in (Get-ChildItem $regClass -ErrorAction SilentlyContinue)) {
                        $p = Get-ItemProperty $sub.PSPath -ErrorAction SilentlyContinue
                        if ($p.DriverDesc -and $p.DriverDesc.Trim() -eq $n) {
                            foreach ($propName in @("TGP", "TotalGraphicsPower", "PowerLimit", "MaxPowerLimit", "PowerTarget", "PP_TargetTGP")) {
                                if ($p.$propName) {
                                    $val = [double]$p.$propName
                                    if ($val -gt 1000) { $val = [math]::Round($val / 1000, 0) } # mW to W
                                    if ($val -ge 15 -and $val -le 500) {
                                        $wattage = [int]$val
                                        break
                                    }
                                }
                            }
                        }
                        if ($wattage -gt 0) { break }
                    }
                } catch {}
            }

            $itemObj = [PSCustomObject]@{
                Name       = $n
                IsDiscrete = $isDiscrete
                Vram       = $vramGB
                Wattage    = $wattage
                DxMem      = $dxMem
            }

            if ($isDiscrete) {
                $dgpuList += $itemObj
            } else {
                $igpuList += $itemObj
            }
        }

        # Định dạng chuẩn mực theo yêu cầu:
        # LIST GPU:
        # ├── iGPU: Intel(R) UHD Graphics 620 [Shared Memory]
        # └── GPU: NVIDIA RTX 3050 6GB (50W)
        $branchMid  = "$([char]0x251C)$([char]0x2500)$([char]0x2500) " # ├── 
        $branchLast = "$([char]0x2514)$([char]0x2500)$([char]0x2500) " # └── 

        $totalCount = $igpuList.Count + $dgpuList.Count
        $gpuItems = @()
        if ($totalCount -eq 0) {
            $gpuDisplay = "N/A"
        } else {
            $allCards = @()
            # iGPU trước, dGPU sau
            foreach ($item in $igpuList) {
                # Bo nho rieng BIOS cap cho iGPU (Intel: DVMT, AMD: UMA Frame Buffer).
                # Uu tien so doc truc tiep tu DXCore, du phong bang registry DirectX, cuoi cung moi ghi Shared Memory
                $igpuMem = if ($item.DxMem -gt 0) { Format-GpuMemory $item.DxMem } else { Get-DxDedicatedMemory $item.Name }
                $memTag = if ($igpuMem) { $igpuMem } else { "Shared Memory" }
                $gpuItems += [PSCustomObject]@{ Name = $item.Name; Kind = 'Tích hợp'; Memory = $memTag; Power = '' }
                $allCards += @{
                    Prefix = "iGPU"
                    Desc   = "$($item.Name) [$memTag]"
                }
            }

            foreach ($item in $dgpuList) {
                $cleanName = $item.Name -replace '\s+', ' '
                $vStr = if ($item.Vram) {
                    if ($cleanName -notmatch "$($item.Vram)") { " $($item.Vram)" } else { "" }
                } else { "" }
                $wStr = if ($item.Wattage -gt 0) { " ($($item.Wattage)W)" } else { "" }
                $gpuItems += [PSCustomObject]@{ Name = $cleanName; Kind = 'Rời'; Memory = $item.Vram; Power = if ($item.Wattage -gt 0) { "$($item.Wattage) W" } else { '' } }

                $prefix = if ($dgpuList.Count -gt 1) { "GPU $($allCards.Count - $igpuList.Count + 1)" } else { "GPU" }

                $allCards += @{
                    Prefix = $prefix
                    Desc   = "$cleanName$vStr$wStr"
                }
            }

            $lines = @("LIST GPU:")
            for ($i = 0; $i -lt $allCards.Count; $i++) {
                $branch = if ($i -eq ($allCards.Count - 1)) { $branchLast } else { $branchMid }
                $c = $allCards[$i]
                $lines += "$branch$($c.Prefix): $($c.Desc)"
            }

            $gpuDisplay = $lines -join "`n"
        }

        # 8. Storage (Trình bày dạng cây phân nhánh khoa học tương tự RAM)
        if ($onProgress) { & $onProgress "[5/5] Đang kiểm tra ổ đĩa lưu trữ & màn hình..." 100 }
        # Danh sach o vat ly (ten, dung luong, chuan ket noi) la phan cung, gan nhu khong doi,
        # nen khi tu dong lam moi thi dung lai ket qua cu. Dung luong trong van doc moi qua DriveInfo ben duoi.
        # Ly do: Get-PhysicalDisk giu lai khoang 9 handle he thong moi lan goi, goi lap lai lien tuc se ro ri dan.
        if ($useCache -and $global:InfoCache.ContainsKey('PhysicalDisks')) {
            $pDisks = @($global:InfoCache['PhysicalDisks'])
        } else {
            $pDisks = @(Get-PhysicalDisk -ErrorAction SilentlyContinue)
            if ($pDisks.Count -eq 0) {
                $pDisks = @(Get-CimInstance Win32_DiskDrive -ErrorAction SilentlyContinue | Select-Object @{N='FriendlyName';E={$_.Model}}, Size, @{N='DeviceId';E={$_.Index}}, @{N='MediaType';E={'SSD'}}, @{N='BusType';E={'NVMe'}}, @{N='InfoConnectionKnown';E={$false}})
            }
            $global:InfoCache['PhysicalDisks'] = $pDisks
        }

        $allParts = @(Get-Partition -ErrorAction SilentlyContinue | Where-Object { $_.DriveLetter })
        $drives = @([System.IO.DriveInfo]::GetDrives() | Where-Object { $_.DriveType -eq [System.IO.DriveType]::Fixed -and $_.IsReady })

        $branchMid  = "$([char]0x251C)$([char]0x2500)$([char]0x2500) " # ├── 
        $branchLast = "$([char]0x2514)$([char]0x2500)$([char]0x2500) " # └── 

        $diskBlocks = @()
        $storageItems = @()

        foreach ($d in $pDisks) {
            $sizeBytes = $d.Size
            # Quy dung luong thuc te ve dung luong ghi tren nhan o cung (chuan thap phan cua nha san xuat).
            # Cach cu chan tren o 2TB nen o 4TB / 8TB deu bi hien sai thanh 2TB.
            $sizeDecGB = $sizeBytes / 1e9
            $stdSizes = @(16, 32, 64, 120, 128, 240, 256, 320, 480, 500, 512, 750, 960, 1000, 1024,
                          2000, 3000, 4000, 5000, 6000, 8000, 10000, 12000, 14000, 16000, 18000, 20000, 22000, 24000)
            $bestSize = 0
            $bestDiff = [double]::MaxValue
            foreach ($s in $stdSizes) {
                $diff = [math]::Abs($sizeDecGB - $s) / $s
                if ($diff -lt $bestDiff) { $bestDiff = $diff; $bestSize = $s }
            }
            if ($bestSize -le 0 -or $bestDiff -gt 0.12) {
                $bestSize = [math]::Round($sizeDecGB, 0)
            }
            $tag = if ($bestSize -ge 1000) {
                $tbVal = $bestSize / 1000.0
                if (($tbVal % 1) -eq 0) { "$([int]$tbVal)TB" } else { "$([math]::Round($tbVal, 1))TB" }
            } else {
                "$([int]$bestSize)GB"
            }
            
            $busTag = if ($d.BusType -and $d.BusType -notmatch "Unknown") { $d.BusType } else { "" }
            $mediaTag = if ($d.MediaType -and $d.MediaType -notmatch "Unspecified|Unknown") { $d.MediaType } else { "Disk" }
            $typeLabel = if ($busTag -and $mediaTag) { "$busTag $mediaTag" } elseif ($busTag) { $busTag } else { $mediaTag }
            
            $header = "$($d.FriendlyName) - $tag"
            if ($typeLabel -and ($d.FriendlyName -notmatch $busTag)) {
                $header = "$($d.FriendlyName) - $tag [$typeLabel]"
            }

            # Tìm các phân vùng thuộc ổ đĩa vật lý này
            $diskNum = $d.DeviceId
            $diskPartLetters = @($allParts | Where-Object { $_.DiskNumber -eq $diskNum } | ForEach-Object { [string]$_.DriveLetter })
            
            $matchedDrives = @()
            if ($diskPartLetters.Count -gt 0) {
                $matchedDrives = @($drives | Where-Object { $diskPartLetters -contains $_.Name[0].ToString() })
            } elseif ($pDisks.Count -eq 1) {
                $matchedDrives = @($drives)
            }

            # Tính độ dài lớn nhất của phần dung lượng free để căn lề thẳng hàng khi copy text
            $maxFreeLen = 0
            foreach ($dr in $matchedDrives) {
                $fLen = ("$([math]::Round($dr.TotalFreeSpace / 1GB, 0))GB").Length
                if ($fLen -gt $maxFreeLen) { $maxFreeLen = $fLen }
            }

            $treeLines = @()
            $partItems = @()
            for ($i = 0; $i -lt $matchedDrives.Count; $i++) {
                $dr = $matchedDrives[$i]
                $branch = if ($i -eq ($matchedDrives.Count - 1)) { $branchLast } else { $branchMid }
                $letter = $dr.Name.TrimEnd('\').TrimEnd(':')
                $freeGB = [math]::Round($dr.TotalFreeSpace / 1GB, 0)
                $totalGB = [math]::Round($dr.TotalSize / 1GB, 0)
                $freeStr = "$($freeGB)GB".PadRight($maxFreeLen)
                $treeLines += "$branch" + "Disk $($letter): $freeStr free / $($totalGB)GB"

                $partItems += [PSCustomObject]@{
                    Branch  = $branch
                    Letter  = $letter
                    FreeGB  = $freeGB
                    TotalGB = $totalGB
                    FreeBytes = $dr.TotalFreeSpace
                    TotalBytes = $dr.TotalSize
                }
            }

            $storageItems += [PSCustomObject]@{
                Header     = $header
                Model      = $d.FriendlyName
                Capacity   = if ($sizeBytes -gt 0) { $tag } else { '' }
                Connection = if ($d.PSObject.Properties['InfoConnectionKnown'] -and -not $d.InfoConnectionKnown) { '' } else { $busTag }
                Partitions = $partItems
            }

            if ($treeLines.Count -gt 0) {
                $diskBlocks += ($header, ($treeLines -join "`n")) -join "`n"
            } else {
                $diskBlocks += $header
            }
        }

        # Nếu không có ổ đĩa vật lý nào map được, dự phòng qua danh sách phân vùng
        if ($diskBlocks.Count -eq 0) {
            $maxFreeLen = 0
            foreach ($dr in $drives) {
                $fLen = ("$([math]::Round($dr.TotalFreeSpace / 1GB, 0))GB").Length
                if ($fLen -gt $maxFreeLen) { $maxFreeLen = $fLen }
            }

            $treeLines = @()
            $partItems = @()
            for ($i = 0; $i -lt $drives.Count; $i++) {
                $dr = $drives[$i]
                $branch = if ($i -eq ($drives.Count - 1)) { $branchLast } else { $branchMid }
                $letter = $dr.Name.TrimEnd('\').TrimEnd(':')
                $freeGB = [math]::Round($dr.TotalFreeSpace / 1GB, 0)
                $totalGB = [math]::Round($dr.TotalSize / 1GB, 0)
                $freeStr = "$($freeGB)GB".PadRight($maxFreeLen)
                $treeLines += "$branch" + "Disk $($letter): $freeStr free / $($totalGB)GB"

                $partItems += [PSCustomObject]@{
                    Branch  = $branch
                    Letter  = $letter
                    FreeGB  = $freeGB
                    TotalGB = $totalGB
                    FreeBytes = $dr.TotalFreeSpace
                    TotalBytes = $dr.TotalSize
                }
            }
            $storageItems += [PSCustomObject]@{
                Header     = "Storage"
                Partitions = $partItems
            }
            $diskBlocks += ("Storage", ($treeLines -join "`n")) -join "`n"
        }

        $storageDisplay = $diskBlocks -join "`n"

        # 9. Resolution (Độ phân giải màn hình chính)
        $bounds = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
        $resDisplay = "$($bounds.Width)x$($bounds.Height)"

# 10. Refresh Rate (Hiển thị tần số quét cao nhất được hỗ trợ của màn hình)
        $hz = 0
        try {
            $primaryDev = [System.Windows.Forms.Screen]::PrimaryScreen.DeviceName
            $hz = [DisplayHelper]::GetMaxRefreshRate($primaryDev)
            if (-not $hz -or $hz -le 0) {
                $hz = [DisplayHelper]::GetMaxRefreshRate($null)
            }
        } catch {}

        # Đối chiếu bổ sung từ WMI Monitor (bảng EDID gốc của phần cứng màn hình)
        try {
            $wmiModes = Get-CimInstance -Namespace root\wmi -ClassName WmiMonitorListedSupportedSourceModes -ErrorAction SilentlyContinue
            if ($wmiModes) {
                foreach ($inst in $wmiModes) {
                    foreach ($m in $inst.MonitorSourceModes) {
                        $num = $m.VerticalRefreshRateNumerator
                        $den = $m.VerticalRefreshRateDenominator
                        $wmiHz = if ($den -and $den -gt 0) { [math]::Round($num / $den, 0) } else { [int]$num }
                        if ($wmiHz -ge 24 -and $wmiHz -le 540 -and $wmiHz -gt $hz) {
                            $hz = $wmiHz
                        }
                    }
                }
            }
        } catch {}

        if (-not $hz -or $hz -le 0) {
            $activeGpu = $filteredGpus | Where-Object { $_.CurrentRefreshRate -gt 0 } | Select-Object -First 1
            if ($activeGpu) { $hz = [math]::Round([double]$activeGpu.CurrentRefreshRate, 0) } else { $hz = 60 }
        }
        $refreshDisplay = "$($hz) Hz"

        if ($AsJson) {
            # Information uses supported maxima, not the active resolution or guessed 60 Hz.
            $resDisplay = '—'
            $refreshDisplay = '—'
            try {
                $displayName = [System.Windows.Forms.Screen]::PrimaryScreen.DeviceName
                $maximum = [DisplayHelper]::GetMaximumMode($displayName)
                if ($maximum[0] -gt 0 -and $maximum[1] -gt 0) { $resDisplay = "$($maximum[0]) × $($maximum[1])" }
                if ($maximum[2] -gt 1) { $refreshDisplay = "$($maximum[2]) Hz" }
            } catch {}
        }

        # 11. Date and Time
        $dateTimeDisplay = (Get-Date).ToString("yyyy-MM-dd HH:mm:ss")

        return [PSCustomObject]@{
            OS           = $osDisplay
            IsActivated  = $isActivated
            Hostname     = $hostDisplay
            Manufacturer = [string]$csObj.Manufacturer
            Model        = $modelDisplay
            Serial       = $serialDisplay
            CPU          = $cpuDisplay
            RAM          = $ramDisplay
            RamTotal     = if ($totalRAMGB -gt 0) { "$totalRAMGB GB" } else { '' }
            RamItems     = @($ramItems)
            GraphicsCard = $gpuDisplay
            GpuItems     = @($gpuItems)
            Storage      = $storageDisplay
            StorageItems = $storageItems
            Resolution   = $resDisplay
            RefreshRate  = $refreshDisplay
            DateTime     = $dateTimeDisplay
        }
    }
    catch {
        Write-Warning "Lỗi khi lấy thông số: $_"
        return $null
    }
}

if ($AsJson) {
    $WarningPreference = 'SilentlyContinue'
    $result = Get-SystemData | Where-Object { $_ -and $_.PSObject.Properties['OS'] } | Select-Object -Last 1
    if (-not $result) { throw 'Không đọc được thông tin hệ thống.' }
    $result | ConvertTo-Json -Depth 8 -Compress
    return
}

# --- XAML GIAO DIỆN BẢNG CHUẨN CỔ ĐIỂN ---
$xaml = @'
<Window xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="System Information"
        SizeToContent="WidthAndHeight"
        ResizeMode="CanMinimize"
        WindowStartupLocation="CenterScreen"
        Background="#FFFFFF"
        Foreground="#000000"
        FontFamily="Segoe UI, Tahoma, Arial"
        FontSize="14">

    <Window.Resources>
        <SolidColorBrush x:Key="BorderColor" Color="#9CBCE8"/>
        <SolidColorBrush x:Key="HeaderBg" Color="#E5E7EB"/>
        <SolidColorBrush x:Key="HighlightBg" Color="#DFECF8"/>

        <!-- Cell Border Left (Property Column) -->
        <Style x:Key="PropCell" TargetType="Border">
            <Setter Property="BorderBrush" Value="{StaticResource BorderColor}"/>
            <Setter Property="BorderThickness" Value="0,0,1,1"/>
            <Setter Property="Padding" Value="12,7"/>
            <Setter Property="Background" Value="Transparent"/>
            <Setter Property="Cursor" Value="Hand"/>
        </Style>

        <!-- Cell Border Right (Value Column) -->
        <Style x:Key="ValCell" TargetType="Border">
            <Setter Property="BorderBrush" Value="{StaticResource BorderColor}"/>
            <Setter Property="BorderThickness" Value="0,0,0,1"/>
            <Setter Property="Padding" Value="12,7"/>
            <Setter Property="Background" Value="Transparent"/>
            <Setter Property="Cursor" Value="Hand"/>
        </Style>

        <!-- Property Label TextBlock -->
        <Style x:Key="PropText" TargetType="TextBlock">
            <Setter Property="FontWeight" Value="Bold"/>
            <Setter Property="Foreground" Value="#000000"/>
            <Setter Property="VerticalAlignment" Value="Center"/>
        </Style>

        <!-- Value Regular TextBlock -->
        <Style x:Key="ValText" TargetType="TextBlock">
            <Setter Property="Foreground" Value="#000000"/>
            <Setter Property="VerticalAlignment" Value="Center"/>
            <Setter Property="TextWrapping" Value="Wrap"/>
        </Style>

        <!-- Value Bold TextBlock -->
        <Style x:Key="ValTextBold" TargetType="TextBlock" BasedOn="{StaticResource ValText}">
            <Setter Property="FontWeight" Value="Bold"/>
        </Style>
    </Window.Resources>

    <!-- KHUNG BẢNG CHÍNH ĐƠN THUẦN, SẠCH SẼ THEO MẪU (HỖ TRỢ THU PHÓNG TỶ LỆ VECTOR) -->
    <Border x:Name="rootBorder" Width="630" BorderBrush="{StaticResource BorderColor}" BorderThickness="1,1,1,0" Margin="0">
        <Border.LayoutTransform>
            <ScaleTransform x:Name="uiScale" ScaleX="1.0" ScaleY="1.0"/>
        </Border.LayoutTransform>
        <Grid x:Name="gridTable">
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="155"/>
                <ColumnDefinition Width="*"/>
            </Grid.ColumnDefinitions>

            <Grid.RowDefinitions>
                <RowDefinition Height="Auto"/> <!-- 0: Header -->
                <RowDefinition Height="Auto"/> <!-- 1: OS -->
                <RowDefinition Height="Auto"/> <!-- 2: Hostname -->
                <RowDefinition Height="Auto"/> <!-- 3: Model -->
                <RowDefinition Height="Auto"/> <!-- 4: Serial -->
                <RowDefinition Height="Auto"/> <!-- 5: CPU (Highlighted Blue) -->
                <RowDefinition Height="Auto"/> <!-- 6: RAM (Highlighted Blue) -->
                <RowDefinition Height="Auto"/> <!-- 7: Graphics Card (Highlighted Blue, Dưới RAM) -->
                <RowDefinition Height="Auto"/> <!-- 8: Storage (Dưới Graphics Card) -->
                <RowDefinition Height="Auto"/> <!-- 9: Resolution -->
                <RowDefinition Height="Auto"/> <!-- 10: Refresh Rate -->
                <RowDefinition Height="Auto"/> <!-- 11: Date and Time -->
            </Grid.RowDefinitions>

            <!-- ROW 0: HEADER -->
            <Border Grid.Row="0" Grid.Column="0" Background="{StaticResource HeaderBg}" Style="{StaticResource PropCell}" Cursor="Arrow">
                <TextBlock Text="Property" FontWeight="Bold" FontSize="14"/>
            </Border>
            <Border Grid.Row="0" Grid.Column="1" Background="{StaticResource HeaderBg}" Style="{StaticResource ValCell}" Cursor="Arrow">
                <TextBlock Text="Value" FontWeight="Bold" FontSize="14"/>
            </Border>

            <!-- ROW 1: OS (Chấm tròn biểu thị trạng thái bản quyền Windows) -->
            <Border x:Name="cellOS_P" Grid.Row="1" Grid.Column="0" Style="{StaticResource PropCell}">
                <TextBlock Text="OS" Style="{StaticResource PropText}"/>
            </Border>
            <Border x:Name="cellOS_V" Grid.Row="1" Grid.Column="1" Style="{StaticResource ValCell}">
                <StackPanel Orientation="Horizontal" VerticalAlignment="Center">
                    <TextBlock x:Name="txtOS" Text="Windows" Style="{StaticResource ValText}" Margin="0,0,8,0"/>
                    <Ellipse x:Name="dotActivation" Width="10" Height="10" Fill="#22C55E" VerticalAlignment="Center"/>
                </StackPanel>
            </Border>

            <!-- ROW 2: HOSTNAME -->
            <Border x:Name="cellHost_P" Grid.Row="2" Grid.Column="0" Style="{StaticResource PropCell}">
                <TextBlock Text="Hostname" Style="{StaticResource PropText}"/>
            </Border>
            <Border x:Name="cellHost_V" Grid.Row="2" Grid.Column="1" Style="{StaticResource ValCell}">
                <TextBlock x:Name="txtHostname" Text="--" Style="{StaticResource ValText}"/>
            </Border>

            <!-- ROW 3: MODEL -->
            <Border x:Name="cellModel_P" Grid.Row="3" Grid.Column="0" Style="{StaticResource PropCell}">
                <TextBlock Text="Model" Style="{StaticResource PropText}"/>
            </Border>
            <Border x:Name="cellModel_V" Grid.Row="3" Grid.Column="1" Style="{StaticResource ValCell}">
                <TextBlock x:Name="txtModel" Text="--" Style="{StaticResource ValText}"/>
            </Border>

            <!-- ROW 4: SERIAL -->
            <Border x:Name="cellSerial_P" Grid.Row="4" Grid.Column="0" Style="{StaticResource PropCell}">
                <TextBlock Text="Serial" Style="{StaticResource PropText}"/>
            </Border>
            <Border x:Name="cellSerial_V" Grid.Row="4" Grid.Column="1" Style="{StaticResource ValCell}">
                <TextBlock x:Name="txtSerial" Text="--" Style="{StaticResource ValText}"/>
            </Border>

            <!-- ROW 5: CPU (Highlighted Blue) -->
            <Border x:Name="cellCPU_P" Grid.Row="5" Grid.Column="0" Background="{StaticResource HighlightBg}" Style="{StaticResource PropCell}">
                <TextBlock Text="CPU" Style="{StaticResource PropText}"/>
            </Border>
            <Border x:Name="cellCPU_V" Grid.Row="5" Grid.Column="1" Background="{StaticResource HighlightBg}" Style="{StaticResource ValCell}">
                <TextBlock x:Name="txtCPU" Text="--" Style="{StaticResource ValTextBold}"/>
            </Border>

            <!-- ROW 6: RAM (Highlighted Blue) -->
            <Border x:Name="cellRAM_P" Grid.Row="6" Grid.Column="0" Background="{StaticResource HighlightBg}" Style="{StaticResource PropCell}">
                <TextBlock Text="RAM" Style="{StaticResource PropText}"/>
            </Border>
            <Border x:Name="cellRAM_V" Grid.Row="6" Grid.Column="1" Background="{StaticResource HighlightBg}" Style="{StaticResource ValCell}">
                <TextBlock x:Name="txtRAM" Text="--" Style="{StaticResource ValTextBold}" LineHeight="20"/>
            </Border>

            <!-- ROW 7: GRAPHICS CARD (Highlighted Blue, Ngay dưới RAM) -->
            <Border x:Name="cellGPU_P" Grid.Row="7" Grid.Column="0" Background="{StaticResource HighlightBg}" Style="{StaticResource PropCell}">
                <TextBlock Text="Graphics Card" Style="{StaticResource PropText}"/>
            </Border>
            <Border x:Name="cellGPU_V" Grid.Row="7" Grid.Column="1" Background="{StaticResource HighlightBg}" Style="{StaticResource ValCell}">
                <TextBlock x:Name="txtGPU" Text="--" Style="{StaticResource ValTextBold}" LineHeight="20"/>
            </Border>

            <!-- ROW 8: STORAGE (Ngay dưới Graphics Card) -->
            <Border x:Name="cellStorage_P" Grid.Row="8" Grid.Column="0" Style="{StaticResource PropCell}">
                <TextBlock Text="Storage" Style="{StaticResource PropText}"/>
            </Border>
            <Border x:Name="cellStorage_V" Grid.Row="8" Grid.Column="1" Style="{StaticResource ValCell}">
                <StackPanel x:Name="panelStorage" VerticalAlignment="Center"/>
            </Border>

            <!-- ROW 9: RESOLUTION -->
            <Border x:Name="cellRes_P" Grid.Row="9" Grid.Column="0" Style="{StaticResource PropCell}">
                <TextBlock Text="Resolution" Style="{StaticResource PropText}"/>
            </Border>
            <Border x:Name="cellRes_V" Grid.Row="9" Grid.Column="1" Style="{StaticResource ValCell}">
                <TextBlock x:Name="txtResolution" Text="--" Style="{StaticResource ValText}"/>
            </Border>

            <!-- ROW 10: REFRESH RATE -->
            <Border x:Name="cellRefresh_P" Grid.Row="10" Grid.Column="0" Style="{StaticResource PropCell}">
                <TextBlock Text="Refresh Rate" Style="{StaticResource PropText}"/>
            </Border>
            <Border x:Name="cellRefresh_V" Grid.Row="10" Grid.Column="1" Style="{StaticResource ValCell}">
                <TextBlock x:Name="txtRefreshRate" Text="--" Style="{StaticResource ValText}"/>
            </Border>

            <!-- ROW 11: DATE AND TIME -->
            <Border x:Name="cellDate_P" Grid.Row="11" Grid.Column="0" Style="{StaticResource PropCell}">
                <TextBlock Text="Date and Time" Style="{StaticResource PropText}"/>
            </Border>
            <Border x:Name="cellDate_V" Grid.Row="11" Grid.Column="1" Style="{StaticResource ValCell}">
                <TextBlock x:Name="txtDateTime" Text="--" Style="{StaticResource ValText}"/>
            </Border>
        </Grid>
    </Border>
</Window>
'@

# --- LOAD XAML ---
$reader = [System.Xml.XmlReader]::Create([System.IO.StringReader]::new($xaml))
$window = [System.Windows.Markup.XamlReader]::Load($reader)

$uiScale = $window.FindName("uiScale")
$rootBorder = $window.FindName("rootBorder")

# Tự động tính toán tỷ lệ phóng to tối ưu cho màn hình 2K / 4K (Phương án A)
$screenBounds = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
$sysDpi = try { [NativeDpiHelper]::GetDpiForSystem() } catch { 96 }
$dpiScale = if ($sysDpi -gt 0) { $sysDpi / 96.0 } else { 1.0 }

$autoScale = 1.0
if ($screenBounds.Width -ge 3800 -or $screenBounds.Height -ge 2100) {
    # Màn hình 4K (3840x2160 trở lên): Nếu Windows đang để Scale thấp (<= 125%), tự nhân scale x1.35
    if ($dpiScale -le 1.25) { $autoScale = 1.35 }
    elseif ($dpiScale -le 1.5) { $autoScale = 1.2 }
}
elseif ($screenBounds.Width -ge 2500 -or $screenBounds.Height -ge 1400) {
    # Màn hình 2K (2560x1440):
    if ($dpiScale -le 1.0) { $autoScale = 1.18 }
    elseif ($dpiScale -le 1.25) { $autoScale = 1.1 }
}

$script:currentZoom = $autoScale
if ($uiScale) {
    $uiScale.ScaleX = $autoScale
    $uiScale.ScaleY = $autoScale
}

# --- THIẾT LẬP ICON CỬA SỔ (BIỂU TƯỢNG CHỮ 'i' WINDOWS) ---
try {
    $iconPath = $null
    if ($PSScriptRoot) {
        $candidate = Join-Path $PSScriptRoot "app.ico"
        if (Test-Path $candidate) { $iconPath = $candidate }
    }
    if (-not $iconPath) {
        $baseDir = [System.AppDomain]::CurrentDomain.BaseDirectory
        if ($baseDir) {
            $candidate = Join-Path $baseDir "app.ico"
            if (Test-Path $candidate) { $iconPath = $candidate }
        }
    }
    if (-not $iconPath) {
        $curDir = (Get-Location).Path
        if ($curDir) {
            $candidate = Join-Path $curDir "app.ico"
            if (Test-Path $candidate) { $iconPath = $candidate }
        }
    }

    if ($iconPath -and (Test-Path $iconPath)) {
        $window.Icon = [System.Windows.Media.Imaging.BitmapFrame]::Create([System.Uri]::new((Resolve-Path $iconPath).Path, [System.UriKind]::Absolute))
    } else {
        $proc = [System.Diagnostics.Process]::GetCurrentProcess()
        if ($proc -and $proc.MainModule -and $proc.MainModule.FileName -and ($proc.MainModule.FileName -notmatch "powershell")) {
            $sysIco = [System.Drawing.Icon]::ExtractAssociatedIcon($proc.MainModule.FileName)
            if ($sysIco) {
                $window.Icon = [System.Windows.Interop.Imaging]::CreateBitmapSourceFromHIcon(
                    $sysIco.Handle,
                    [System.Windows.Int32Rect]::Empty,
                    [System.Windows.Media.Imaging.BitmapSizeOptions]::FromEmptyOptions()
                )
            }
        }
    }

    if (-not $window.Icon) {
        $window.Icon = [System.Windows.Interop.Imaging]::CreateBitmapSourceFromHIcon(
            [System.Drawing.SystemIcons]::Information.Handle,
            [System.Windows.Int32Rect]::Empty,
            [System.Windows.Media.Imaging.BitmapSizeOptions]::FromEmptyOptions()
        )
    }
} catch {}

# --- THAM CHIẾU CÁC PHẦN TỬ ---
$txtOS          = $window.FindName("txtOS")
$dotActivation  = $window.FindName("dotActivation")
$txtHostname    = $window.FindName("txtHostname")
$txtModel       = $window.FindName("txtModel")
$txtSerial      = $window.FindName("txtSerial")
$txtCPU         = $window.FindName("txtCPU")
$txtRAM         = $window.FindName("txtRAM")
$txtGPU         = $window.FindName("txtGPU")
$panelStorage   = $window.FindName("panelStorage")
$txtResolution  = $window.FindName("txtResolution")
$txtRefreshRate = $window.FindName("txtRefreshRate")
$txtDateTime    = $window.FindName("txtDateTime")

$script:CurrentData = $null

$brushGreen  = New-Object System.Windows.Media.SolidColorBrush([System.Windows.Media.Color]::FromRgb(34, 197, 94))   # #22C55E Xanh lá: Đã active
$brushOrange = New-Object System.Windows.Media.SolidColorBrush([System.Windows.Media.Color]::FromRgb(249, 115, 22))  # #F97316 Màu cam: Chưa active

function Update-Values($data = $null) {
    if (-not $data) { $data = Get-SystemData }
    # Loc lay dung doi tuong du lieu phong truong hop output stream bi lan gia tri la
    if ($data -is [array]) {
        $data = $data | Where-Object { $_ -and $_.PSObject.Properties['OS'] } | Select-Object -First 1
    }
    if (-not $data) { return }
    $script:CurrentData = $data

    $txtOS.Text = $data.OS

    # Cập nhật màu dot bản quyền: Xanh (Đã active) | Cam (Chưa active)
    if ($data.IsActivated) {
        $dotActivation.Fill = $brushGreen
        $dotActivation.ToolTip = "Đã kích hoạt bản quyền (Activated)"
    } else {
        $dotActivation.Fill = $brushOrange
        $dotActivation.ToolTip = "Chưa kích hoạt bản quyền (Not Activated)"
    }

    $txtHostname.Text    = $data.Hostname
    $txtModel.Text       = $data.Model
    $txtSerial.Text      = $data.Serial
    $txtCPU.Text         = $data.CPU
    $txtRAM.Text         = $data.RAM
    $txtGPU.Text         = $data.GraphicsCard
        # Cập nhật Storage dạng Sub-Grid căn lề tuyệt đối thẳng hàng
    $panelStorage.Children.Clear()
    if ($data.StorageItems) {
        foreach ($item in $data.StorageItems) {
            $tbH = New-Object System.Windows.Controls.TextBlock
            $tbH.Text = $item.Header
            $tbH.Style = $window.FindResource("ValText")
            $tbH.Margin = New-Object System.Windows.Thickness(0, 0, 0, 2)
            [void]$panelStorage.Children.Add($tbH)

            if ($item.Partitions -and $item.Partitions.Count -gt 0) {
                $grid = New-Object System.Windows.Controls.Grid
                $grid.Margin = New-Object System.Windows.Thickness(0, 0, 0, 3)

                # 4 Columns:
                # 0: Branch + "Disk C: " (Auto)
                # 1: Free GB (Right-aligned, Auto)
                # 2: " free / " (Auto)
                # 3: Total GB (Left-aligned, Auto)
                0..3 | ForEach-Object {
                    $col = New-Object System.Windows.Controls.ColumnDefinition
                    $col.Width = [System.Windows.GridLength]::Auto
                    [void]$grid.ColumnDefinitions.Add($col)
                }

                for ($r = 0; $r -lt $item.Partitions.Count; $r++) {
                    $p = $item.Partitions[$r]
                    $row = New-Object System.Windows.Controls.RowDefinition
                    $row.Height = [System.Windows.GridLength]::Auto
                    [void]$grid.RowDefinitions.Add($row)

                    # Col 0: "├── Disk C: "
                    $tb0 = New-Object System.Windows.Controls.TextBlock
                    $tb0.Text = "$($p.Branch)Disk $($p.Letter): "
                    $tb0.Style = $window.FindResource("ValText")
                    $tb0.Margin = New-Object System.Windows.Thickness(0, 1, 0, 1)
                    [System.Windows.Controls.Grid]::SetRow($tb0, $r)
                    [System.Windows.Controls.Grid]::SetColumn($tb0, 0)
                    [void]$grid.Children.Add($tb0)

                    # Col 1: "$($p.FreeGB)GB" - căn phải
                    $tb1 = New-Object System.Windows.Controls.TextBlock
                    $tb1.Text = "$($p.FreeGB)GB"
                    $tb1.TextAlignment = [System.Windows.TextAlignment]::Right
                    $tb1.Style = $window.FindResource("ValText")
                    $tb1.Margin = New-Object System.Windows.Thickness(0, 1, 0, 1)
                    [System.Windows.Controls.Grid]::SetRow($tb1, $r)
                    [System.Windows.Controls.Grid]::SetColumn($tb1, 1)
                    [void]$grid.Children.Add($tb1)

                    # Col 2: " free / "
                    $tb2 = New-Object System.Windows.Controls.TextBlock
                    $tb2.Text = " free / "
                    $tb2.Style = $window.FindResource("ValText")
                    $tb2.Margin = New-Object System.Windows.Thickness(0, 1, 0, 1)
                    [System.Windows.Controls.Grid]::SetRow($tb2, $r)
                    [System.Windows.Controls.Grid]::SetColumn($tb2, 2)
                    [void]$grid.Children.Add($tb2)

                    # Col 3: "$($p.TotalGB)GB"
                    $tb3 = New-Object System.Windows.Controls.TextBlock
                    $tb3.Text = "$($p.TotalGB)GB"
                    $tb3.Style = $window.FindResource("ValText")
                    $tb3.Margin = New-Object System.Windows.Thickness(0, 1, 0, 1)
                    [System.Windows.Controls.Grid]::SetRow($tb3, $r)
                    [System.Windows.Controls.Grid]::SetColumn($tb3, 3)
                    [void]$grid.Children.Add($tb3)
                }
                [void]$panelStorage.Children.Add($grid)
            }
        }
    }
    $txtResolution.Text  = $data.Resolution
    $txtRefreshRate.Text = $data.RefreshRate
    $txtDateTime.Text    = $data.DateTime
}

# Live clock ticking
$timer = New-Object System.Windows.Threading.DispatcherTimer
$timer.Interval = [TimeSpan]::FromSeconds(1)
$timer.Add_Tick({
    $txtDateTime.Text = (Get-Date).ToString("yyyy-MM-dd HH:mm:ss")
})
$timer.Start()

# Timer phục hồi tiêu đề cửa sổ sau khi thông báo copy
$titleTimer = New-Object System.Windows.Threading.DispatcherTimer
$titleTimer.Interval = [TimeSpan]::FromSeconds(2)
$titleTimer.Add_Tick({
    $window.Title = "System Information"
    $titleTimer.Stop()
})

function Notify-Copy ($label, $val) {
    if (-not $val) { return }
    [System.Windows.Clipboard]::SetText($val)

    # Hiển thị trạng thái ngắn gọn trên tiêu đề cửa sổ
    $window.Title = "System Information - Đã sao chép $label"
    $titleTimer.Stop()
    $titleTimer.Start()
}

# Gán sự kiện Double-Click cho cả 2 ô của mỗi dòng (Property & Value)
$rowBorders = @(
    @{ Cells = @($window.FindName("cellOS_P"), $window.FindName("cellOS_V")); Name = "OS"; GetVal = { $script:CurrentData.OS } },
    @{ Cells = @($window.FindName("cellHost_P"), $window.FindName("cellHost_V")); Name = "Hostname"; GetVal = { $script:CurrentData.Hostname } },
    @{ Cells = @($window.FindName("cellModel_P"), $window.FindName("cellModel_V")); Name = "Model"; GetVal = { $script:CurrentData.Model } },
    @{ Cells = @($window.FindName("cellSerial_P"), $window.FindName("cellSerial_V")); Name = "Serial"; GetVal = { $script:CurrentData.Serial } },
    @{ Cells = @($window.FindName("cellCPU_P"), $window.FindName("cellCPU_V")); Name = "CPU"; GetVal = { $script:CurrentData.CPU } },
    @{ Cells = @($window.FindName("cellRAM_P"), $window.FindName("cellRAM_V")); Name = "RAM"; GetVal = { $script:CurrentData.RAM } },
    @{ Cells = @($window.FindName("cellGPU_P"), $window.FindName("cellGPU_V")); Name = "Graphics Card"; GetVal = { $script:CurrentData.GraphicsCard } },
    @{ Cells = @($window.FindName("cellStorage_P"), $window.FindName("cellStorage_V")); Name = "Storage"; GetVal = { $script:CurrentData.Storage } },
    @{ Cells = @($window.FindName("cellRes_P"), $window.FindName("cellRes_V")); Name = "Resolution"; GetVal = { $script:CurrentData.Resolution } },
    @{ Cells = @($window.FindName("cellRefresh_P"), $window.FindName("cellRefresh_V")); Name = "Refresh Rate"; GetVal = { $script:CurrentData.RefreshRate } },
    @{ Cells = @($window.FindName("cellDate_P"), $window.FindName("cellDate_V")); Name = "Date & Time"; GetVal = { $script:CurrentData.DateTime } }
)

foreach ($item in $rowBorders) {
    foreach ($cell in $item.Cells) {
        if ($cell) {
            $cell.Tag = @{ Name = $item.Name; GetVal = $item.GetVal }
            $cell.Add_MouseLeftButtonDown({
                param($s, $e)
                if ($e.ClickCount -ge 2 -and $s.Tag) {
                    $val = & $s.Tag.GetVal
                    if ($val) { Notify-Copy $s.Tag.Name $val }
                }
            })
        }
    }
}

# Hàm lấy toàn bộ văn bản báo cáo
function Get-FullReportText {
    if (-not $script:CurrentData) { return "" }
    $d = $script:CurrentData
    $licText = if ($d.IsActivated) { " (Activated)" } else { " (Not Activated)" }
    return @"
=====================================================
          THÔNG TIN HỆ THỐNG / SYSTEM INFORMATION
=====================================================
OS             : $($d.OS)$licText
Hostname       : $($d.Hostname)
Model          : $($d.Model)
Serial         : $($d.Serial)
CPU            : $($d.CPU)
RAM            :
$($d.RAM)
Graphics Card  :
$($d.GraphicsCard)
Storage        :
$($d.Storage)
Resolution     : $($d.Resolution)
Refresh Rate   : $($d.RefreshRate)
Date and Time  : $($d.DateTime)
=====================================================
"@
}

# --- MENU CHUỘT PHẢI (CONTEXT MENU) ---
$ctxMenu = New-Object System.Windows.Controls.ContextMenu

# 1. Copy toàn bộ thông số
$miCopyAll = New-Object System.Windows.Controls.MenuItem
$miCopyAll.Header = "Sao chép toàn bộ thông số (Ctrl+C)"
$miCopyAll.Add_Click({
    $txt = Get-FullReportText
    if ($txt) {
        [System.Windows.Clipboard]::SetText($txt)
        $window.Title = "System Information - Đã sao chép toàn bộ!"
        $titleTimer.Stop()
        $titleTimer.Start()
    }
})
$ctxMenu.Items.Add($miCopyAll) | Out-Null

# 2. Xuất file báo cáo .txt
$miExport = New-Object System.Windows.Controls.MenuItem
$miExport.Header = "Xuất file báo cáo cấu hình (.txt)..."
$miExport.Add_Click({
    $txt = Get-FullReportText
    if ($txt) {
        $sfd = New-Object System.Windows.Forms.SaveFileDialog
        $sfd.Filter = "Text Documents (*.txt)|*.txt|All Files (*.*)|*.*"
        $sfd.FileName = "SystemInfo_$($script:CurrentData.Hostname)_$((Get-Date).ToString('yyyyMMdd')).txt"
        $sfd.Title = "Lưu báo cáo cấu hình máy"
        if ($sfd.ShowDialog() -eq [System.Windows.Forms.DialogResult]::OK) {
            [System.IO.File]::WriteAllText($sfd.FileName, $txt, [System.Text.Encoding]::UTF8)
            $window.Title = "System Information - Đã lưu file .txt!"
            $titleTimer.Stop()
            $titleTimer.Start()
        }
    }
})
$ctxMenu.Items.Add($miExport) | Out-Null

# Hàm điều chỉnh mức thu phóng bảng (Phương án B: Interactive Zoom)
function Set-UiZoom($newZoom) {
    $clamped = [math]::Max(0.7, [math]::Min(2.2, [math]::Round($newZoom, 2)))
    $script:currentZoom = $clamped
    if ($uiScale) {
        $uiScale.ScaleX = $clamped
        $uiScale.ScaleY = $clamped
    }
    $window.Title = "System Information - Thu phóng: $([math]::Round($clamped * 100))%"
    $titleTimer.Stop()
    $titleTimer.Start()
}

# Thu phóng bảng bằng chuột: Giữ Ctrl + Lăn con lăn chuột
$window.Add_PreviewMouseWheel({
    param($s, $e)
    if ([System.Windows.Input.Keyboard]::Modifiers -band [System.Windows.Input.ModifierKeys]::Control) {
        $e.Handled = $true
        $delta = if ($e.Delta -gt 0) { 0.05 } else { -0.05 }
        Set-UiZoom ($script:currentZoom + $delta)
    }
})

$ctxMenu.Items.Add((New-Object System.Windows.Controls.Separator)) | Out-Null

# 3. Thu phóng giao diện trong menu chuột phải
$miZoomIn = New-Object System.Windows.Controls.MenuItem
$miZoomIn.Header = "Phóng to (Ctrl + hoặc Ctrl + Lăn chuột)"
$miZoomIn.Add_Click({ Set-UiZoom ($script:currentZoom + 0.1) })
$ctxMenu.Items.Add($miZoomIn) | Out-Null

$miZoomOut = New-Object System.Windows.Controls.MenuItem
$miZoomOut.Header = "Thu nhỏ (Ctrl -)"
$miZoomOut.Add_Click({ Set-UiZoom ($script:currentZoom - 0.1) })
$ctxMenu.Items.Add($miZoomOut) | Out-Null

$miZoomReset = New-Object System.Windows.Controls.MenuItem
$miZoomReset.Header = "Đặt lại kích thước chuẩn (Ctrl 0)"
$miZoomReset.Add_Click({ Set-UiZoom 1.0 })
$ctxMenu.Items.Add($miZoomReset) | Out-Null

$ctxMenu.Items.Add((New-Object System.Windows.Controls.Separator)) | Out-Null

# 4. Làm mới (thu cong: quet lai toan bo; tu dong moi 60 giay: xem phan cuoi file)
$miRefresh = New-Object System.Windows.Controls.MenuItem
$miRefresh.Header = "Làm mới thông số (F5) - tự động mỗi 60 giây"
$miRefresh.Add_Click({ Start-DataRefresh $true })
$ctxMenu.Items.Add($miRefresh) | Out-Null

$window.ContextMenu = $ctxMenu

# Hỗ trợ phím tắt: Ctrl+C (Copy All), F5 (Refresh), Ctrl + / - / 0 (Thu phóng)
$window.Add_KeyDown({
    param($s, $e)
    if ($e.Key -eq [System.Windows.Input.Key]::F5) {
        Start-DataRefresh $true
    }
    elseif ($e.Key -eq [System.Windows.Input.Key]::C -and ([System.Windows.Input.Keyboard]::Modifiers -band [System.Windows.Input.ModifierKeys]::Control)) {
        $txt = Get-FullReportText
        if ($txt) {
            [System.Windows.Clipboard]::SetText($txt)
            $window.Title = "System Information - Đã sao chép toàn bộ!"
            $titleTimer.Stop()
            $titleTimer.Start()
        }
    }
    elseif ([System.Windows.Input.Keyboard]::Modifiers -band [System.Windows.Input.ModifierKeys]::Control) {
        if ($e.Key -eq [System.Windows.Input.Key]::OemPlus -or $e.Key -eq [System.Windows.Input.Key]::Add) {
            Set-UiZoom ($script:currentZoom + 0.1)
        }
        elseif ($e.Key -eq [System.Windows.Input.Key]::OemMinus -or $e.Key -eq [System.Windows.Input.Key]::Subtract) {
            Set-UiZoom ($script:currentZoom - 0.1)
        }
        elseif ($e.Key -eq [System.Windows.Input.Key]::D0 -or $e.Key -eq [System.Windows.Input.Key]::NumPad0) {
            Set-UiZoom 1.0
        }
    }
})

# Quét thông số phần cứng ngầm trong Runspace độc lập để thanh loading chạy mượt mà 1 => 100
$funcsToExport = @("Resolve-JedecId", "Resolve-RamManufacturer", "Resolve-DdrType", "Get-SmbiosMemoryDevices", "Get-WindowsActivationStatus", "Get-GpuVram", "Get-DxDedicatedMemory", "Format-GpuMemory", "Get-DxCoreGpuInfo", "Test-GpuDiscreteByName", "Get-SystemData")
$sbWorker = New-Object System.Text.StringBuilder
[void]$sbWorker.AppendLine("Add-Type -AssemblyName System.Windows.Forms, System.Drawing")
foreach ($fn in $funcsToExport) {
    $funcDef = Get-Content "Function:\$fn"
    [void]$sbWorker.AppendLine("function $fn { $funcDef }")
}

$workerCode = $sbWorker.ToString()

# Runspace nen dung chung cho lan tai dau va moi lan lam moi ve sau.
# Cac ham chi nap mot lan, va bo nho dem ($global:InfoCache) duoc giu lai giua cac lan quet.
$script:bgRunspace = [runspacefactory]::CreateRunspace()
$script:bgRunspace.Open()

$psWorker = [powershell]::Create()
$psWorker.Runspace = $script:bgRunspace
[void]$psWorker.AddScript($workerCode, $false)        # $false: nap ham vao pham vi toan cuc cua runspace
[void]$psWorker.AddScript('Get-SystemData', $false)

$handleWorker = $psWorker.BeginInvoke()

# Vòng lặp tiến trình chạy liên tục từ 1 => 100 không ngừng nghỉ, khẩn trương và sống động
$pct = 1
while ($pct -le 100) {
    $txtPercent.Text = "$pct%"
    $progBar.Value = $pct
    [System.Windows.Threading.Dispatcher]::CurrentDispatcher.Invoke([Action]{}, [System.Windows.Threading.DispatcherPriority]::Render)

    if (-not $handleWorker.IsCompleted) {
        if ($pct -lt 92) {
            $pct++
            Start-Sleep -Milliseconds 22
        } else {
            Start-Sleep -Milliseconds 35
        }
    } else {
        $pct++
        Start-Sleep -Milliseconds 8
    }
}

$resWorker = $psWorker.EndInvoke($handleWorker)

# Chi nhan dung doi tuong du lieu, bo qua moi gia tri rac lot vao output stream cua runspace.
# Truoc day lay thang $resWorker[0] nen de vo phai gia tri Boolean => bang trong, phai bam F5 moi len.
$systemData = $null
if ($resWorker) {
    foreach ($r in $resWorker) {
        if ($r -and $r.PSObject.Properties['OS']) { $systemData = $r; break }
    }
}
$psWorker.Dispose()

# Nếu có sự cố ngoài dự kiến trong Runspace, tự động truy vấn dự phòng trực tiếp
if (-not $systemData) {
    $systemData = Get-SystemData
}

Start-Sleep -Milliseconds 60

# Đóng màn hình loading
$splash.Close()

# Đổ dữ liệu đã nạp sẵn vào cửa sổ chính (hiển thị lập tức, không delay, không giật)
Update-Values $systemData

# --- TU DONG LAM MOI MOI 60 GIAY ---
# Viec quet chay o runspace nen de cua so khong bi treo. Mot dong ho 200ms kiem tra khi nao quet xong
# roi moi cap nhat giao dien tren luong chinh.
#   - Tu dong: dung lai ket qua cua cac phep do nang, khong doi (ban quyen, nvidia-smi, DXCore),
#     chi doc moi dung luong o dia, RAM, man hinh... Khong hien thong bao tren tieu de.
#   - Thu cong (F5 / menu): quet lai toan bo, hien thong bao, va dem lai 60 giay tu dau.
#   - Gioi han: tu dong toi da 10 lan (10 phut) roi dung. Lan thu cong khong tinh vao gioi han,
#     va bam F5 se bat lai tu dong lam moi voi luot dem moi 10 lan.
$script:bgPS = $null
$script:bgHandle = $null
$script:bgIsManual = $false
$script:bgManualPending = $false

$script:autoRefreshSeconds = 60
$script:autoRefreshMax = 10
$script:autoRefreshCount = 0
$script:refreshHeaderOn  = "Làm mới thông số (F5) - tự động mỗi $($script:autoRefreshSeconds) giây"
$script:refreshHeaderOff = "Làm mới thông số (F5) - đã dừng tự động, bấm F5 để bật lại"
$miRefresh.Header = $script:refreshHeaderOn   # menu luon khop voi so giay cau hinh o tren

$autoTimer = New-Object System.Windows.Threading.DispatcherTimer
$autoTimer.Interval = [TimeSpan]::FromSeconds($script:autoRefreshSeconds)

$pollTimer = New-Object System.Windows.Threading.DispatcherTimer
$pollTimer.Interval = [TimeSpan]::FromMilliseconds(200)

function Start-DataRefresh([bool]$manual) {
    if ($script:bgHandle) {
        # Dang co mot lan quet chay nen: ghi nho yeu cau thu cong de chay ngay khi lan nay xong
        if ($manual) { $script:bgManualPending = $true }
        return
    }

    $script:bgIsManual = $manual
    if ($manual) {
        $titleTimer.Stop()
        $window.Title = "System Information - Đang làm mới..."
        # Bat lai tu dong lam moi voi luot dem moi, va dem lai 60 giay tu bay gio
        $script:autoRefreshCount = 0
        $miRefresh.Header = $script:refreshHeaderOn
        $autoTimer.Stop()
        $autoTimer.Start()
    } else {
        # Chi dem nhung lan tu dong thuc su duoc chay
        $script:autoRefreshCount++
        if ($script:autoRefreshCount -ge $script:autoRefreshMax) {
            $autoTimer.Stop()
            $miRefresh.Header = $script:refreshHeaderOff
        }
    }

    try {
        $ps = [powershell]::Create()
        $ps.Runspace = $script:bgRunspace
        [void]$ps.AddCommand('Get-SystemData').AddParameter('useCache', (-not $manual))
        $script:bgPS = $ps
        $script:bgHandle = $ps.BeginInvoke()
        $pollTimer.Start()
    } catch {
        $script:bgPS = $null
        $script:bgHandle = $null
    }
}

$pollTimer.Add_Tick({
    if (-not $script:bgHandle -or -not $script:bgHandle.IsCompleted) { return }
    $pollTimer.Stop()

    $res = $null
    try { $res = $script:bgPS.EndInvoke($script:bgHandle) } catch {}
    try { $script:bgPS.Dispose() } catch {}
    $script:bgPS = $null
    $script:bgHandle = $null

    $data = $null
    if ($res) {
        foreach ($r in $res) {
            if ($r -and $r.PSObject.Properties['OS']) { $data = $r; break }
        }
    }
    if ($data) { Update-Values $data }

    if ($script:bgIsManual) {
        $window.Title = if ($data) { "System Information - Đã làm mới!" } else { "System Information - Làm mới thất bại" }
        $titleTimer.Stop()
        $titleTimer.Start()
    }

    if ($script:bgManualPending) {
        $script:bgManualPending = $false
        Start-DataRefresh $true
    }
})

$autoTimer.Add_Tick({
    if ($script:autoRefreshCount -ge $script:autoRefreshMax) { $autoTimer.Stop(); return }
    Start-DataRefresh $false
})

# Dong cua so: dung cac dong ho va yeu cau dung lan quet dang chay (khong cho, de thoat ngay)
$window.Add_Closed({
    $autoTimer.Stop()
    $pollTimer.Stop()
    $timer.Stop()
    if ($script:bgPS) { try { [void]$script:bgPS.BeginStop($null, $null) } catch {} }
})

$autoTimer.Start()

# Hiển thị cửa sổ chính
[void]$window.ShowDialog()
