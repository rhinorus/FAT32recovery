using System;
using System.Text;
using System.Windows;
using System.Windows.Forms;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using System.IO;
using System.Collections.Generic;
using System.Threading;

namespace FilesUndelitingFAT32
{
    /// <summary>
    /// Логика взаимодействия для MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        int BOOT_SECTOR_SIZE = 512;

        long MAX_RECOVERING_FILE_SIZE = 1024 * 1024;

        string RECOVERY_DIRECTORY = "/"; //адрес директории для восстановления.

        DriveDirectory rootRecover; //отсканированный корень флешки.

        //переменные, нужные для того, чтобы прыгать по файловой зоне.
        long ClasterSize;
        long StartOfFileData;

        //тут хранится первая копия FAT. Хранить её проще и быстрее, чем постоянно обращаться. 
        //Занимает в районе 20мб оперативки для больших флешек.
        byte[] FAT;

        SafeFileHandle DiskHandle = null;
        FileStream diskStreamToRead;

        //метод чтения по кластерам
        private byte[] Read(long offset, int length)
        {
            byte[] buf = new byte[length];

            diskStreamToRead.Position = StartOfFileData;
            diskStreamToRead.Position += offset;
            diskStreamToRead.Read(buf, 0, length);

            return buf;
        }

        //метод для чтения области FAT
        private byte[] ReadFAT(long offset, int length)
        {
            byte[] buf = new byte[length];

            diskStreamToRead.Position = 0; //область FAT читаем с нуля
            diskStreamToRead.Position += offset;
            diskStreamToRead.Read(buf, 0, length);

            return buf;
        }

        //собирает числа из байт.
        private long HexToDecimal(byte[] arr)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append(Hex(arr[0]));

            for (int i = 1; i < arr.Length; i++)
                sb.Insert(0, Hex(arr[i]));

            long decValue = Convert.ToInt64(sb.ToString(), 16);

            return decValue;
        }

        private long HexToDecimal(byte[] Data, int startIndex, int EndIndex)
        {
            byte[] bytes = new byte[EndIndex - startIndex + 1];

            for (int i = startIndex; i <= EndIndex; i++)
                bytes[i - startIndex] = Data[i];

            return HexToDecimal(bytes);

        }

        //метод для вычисления размеров файла. Очень полезный.
        private long CalculateSize(byte[] Data, int startIndex, int EndIndex)
        {
            StringBuilder stringBytes = new StringBuilder("");

            for (int i = startIndex; i <= EndIndex; i++) //байты пишем в обратном порядке. Так надо. Вычислено эмпирическим путём.
                stringBytes.Insert(0,Hex(Data[i]));

            return Convert.ToInt64(stringBytes.ToString(), 16);
        }

        //метод, возвращающий строку. Для LFN.
        private string DecimalToUnicode(byte[] arr)
        {
            return Encoding.Unicode.GetString(arr);
        }

        private string DecimalToUnicode(byte[] Data, int startIndex, int EndIndex)
        {
            byte[] bytes = new byte[EndIndex - startIndex + 1];

            for (int i = startIndex; i <= EndIndex; i++)
                bytes[i - startIndex] = Data[i];

            return DecimalToUnicode(bytes);
        }

        //метод, возвращающий строку. Для коротких имён.
        private string DecimalToASCII(byte[] arr)
        {
            return Encoding.ASCII.GetString(arr);

        }

        private string DecimalToASCII(byte[] Data, int startIndex, int EndIndex)
        {
            byte[] bytes = new byte[EndIndex - startIndex + 1];

            for (int i = startIndex; i <= EndIndex; i++)
                bytes[i - startIndex] = Data[i];

            return DecimalToASCII(bytes);
        }

        //метод, приводящий десятичное число к шестнадцатеричному.
        private string Hex(byte b)
        {
            var str = Convert.ToString(b, 16);

            //добавляем незначащие нули. Они нужны.
            if (str.Length == 1)
                str = str.Insert(0, "0");

            return str;
        }

        //метод, который распечатывает массив байт в файл output
        private void OutPutData(byte[] Data)
        {
            StreamWriter sw = new StreamWriter("output.txt");

            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < Data.Length; i++)
            {
                if (i != 0 && i % 16 == 0)
                {
                    sw.WriteLine(sb.ToString());
                    sb.Clear();
                }

                sb.Append(Hex(Data[i]) + " ");
            }
            sw.WriteLine(sb.ToString());

            sw.Close();
        }

        //метод, который говорит, свободен ли указанный кластер.
        private bool IsClasterFreeInFAT(int claster)
        {
            int offset = 4 * claster;

            if (HexToDecimal(FAT, offset, offset + 3) == 0)
                return true;
            else
                return false;
        }

        //возвращает следующий кластер, на который указывает текущий.
        private long GetNextClaster(int claster)
        {
            int offset = 4 * claster;

            return CalculateSize(FAT, offset, offset + 3);
        }

        //метод, который говорит, является ли кластер конечным
        private bool IsClasterEOF(int claster)
        {
            int offset = 4 * claster;
            if (Hex(FAT[offset + 0]) == "ff" && //именно так выглядит конец файла.
                Hex(FAT[offset + 1]) == "ff" &&
                Hex(FAT[offset + 2]) == "ff" &&
                Hex(FAT[offset + 3]) == "0f")
                return true;
            else
                return false;
        }

        //метод для объединения кластеров в один.
        public byte[] Unify(byte[] a, byte[] b)
        {
            byte[] c = new byte[a.Length + b.Length];
            for (int i = 0; i < a.Length; i++)
                c[i] = a[i];
            for (int j = 0; j < b.Length; j++)
                c[a.Length + j] = b[j];
            return c;
        }

        //нам не нужны лишние юникодовские закорючки в именах файлов.
        public string GetClearFileName(string fileName)
        {
            char[] charsToReplace = new char[] { '\\', '/', ':', '*', '?', '"', '<', '>', '|' };
            StringBuilder sb = new StringBuilder(fileName);

            foreach (var ch in charsToReplace)
                sb.Replace("" + ch, "");
            return sb.ToString();
        }

        //метод, возвращающий атрибут файла
        private FileAttribute GetAttribute(byte b)
        {
            var hexVal = Hex(b);

            switch (hexVal)
            {
                case "00":
                    return FileAttribute.Empty;
                case "01":
                    return FileAttribute.ReadOnly;
                case "02":
                    return FileAttribute.Hidden;
                case "04":
                    return FileAttribute.SystemFile;
                case "08":
                    return FileAttribute.Drive;
                case "0f":
                    return FileAttribute.LFN;
                case "10":
                    return FileAttribute.Directory;
                case "20":
                    return FileAttribute.Archive;
                case "16":
                    return FileAttribute.SystemRegistry; //тоже левый атрибут, не прописанный в спецификации. Встречен у System Volume Information
                case "22":
                    return FileAttribute.StrangeAttribute; //какой-то левый атрибут. Не прописан в спецификации, но встречается.
                default:
                    throw new ContextMarshalException("Некорректный атрибут файла: " + b + " " + hexVal);
            }
        }

        //рекурсивынй метод для восстановления и сборки файлов.
        private void CreateFolderWithFiles(string parentPath, DriveDirectory folder)
        {
            foreach(var dir in folder.Directories) 
            {
                if (!Directory.Exists(parentPath + GetClearFileName(dir.Name)))
                    Directory.CreateDirectory(parentPath + GetClearFileName(dir.Name));
                CreateFolderWithFiles(parentPath + GetClearFileName(dir.Name) + "\\", dir);
            }

            foreach(var file in folder.Files)
            {
                if(file.Clasters.Count > 0) //не делаем пустые файлы. Нафиг они не нужны.
                {
                    FileStream fs = null;

                    try
                    {
                        fs = new FileStream(parentPath + GetClearFileName(file.Name) ,FileMode.OpenOrCreate);                        

                        for(int i = 0; i< file.Clasters.Count - 1; i++)
                        {
                            var Data = Read((file.Clasters[i] - 2) * ClasterSize, (int)ClasterSize);

                            fs.WriteAsync(Data, 0, (int)ClasterSize);
                        }

                        //у последнего кластера мы убираем все нули в конце. Иначе файл будет некорректно восстановлен.
                        var LastClasterData = Read((file.Clasters[file.Clasters.Count - 1]-2) * ClasterSize, (int)ClasterSize);

                        int countOfZeroBytes = 0;

                        while (LastClasterData[LastClasterData.Length - countOfZeroBytes - 1] == 0)
                            countOfZeroBytes++;

                        fs.Write(LastClasterData, 0, (int)ClasterSize - countOfZeroBytes);

                    }
                    catch (Exception ex)
                    {
                        System.Windows.Forms.MessageBox.Show(ex.Message);
                    }
                    finally
                    {
                        if (fs != null)
                            fs.Close();
                    }
                }
            }
        }

        //рекурсивный метод, строящий цепочку из директорий и содержащихся в них файлов, метод многопоточный.
        private void ProcessDirectory(object dirWithClaster)
        {
            var claster = (dirWithClaster as DirectoryWithClaster).Claster;
            var directory = (dirWithClaster as DirectoryWithClaster).Directory;

            #region calculateDirectoryData

            List<int> ClastersOfThisDirectody = new List<int>(); //как оказалось, директории могут располагаться не в одном кластере.
            List<byte[]> clastersData = new List<byte[]>();

            while (!IsClasterEOF((int)claster) && !IsClasterFreeInFAT((int)claster))
            {
                ClastersOfThisDirectody.Add((int)claster);
                clastersData.Add(Read((claster - 2) * ClasterSize, (int)ClasterSize));
                claster = GetNextClaster((int)claster);
            }
            ClastersOfThisDirectody.Add((int)claster);
            clastersData.Add(Read((claster - 2) * ClasterSize, (int)ClasterSize));

            var Data = clastersData[0]; //данные именно того кластера, который был передан в качестве параметра.

            for (int i = 1; i < clastersData.Count; i++)
                Data = Unify(Data, clastersData[i]);

            #endregion

            //читаем не с нулевой записи, потому что она указывает на текущую директорию. Нафиг надо.
            for (int i = 32; i < Data.Length - 32; i += 32)
            {
                try
                {
                    var attribute = GetAttribute(Data[i + 11]);

                    if (attribute == FileAttribute.Empty) //нечего читать.
                        break;

                    #region shortNameDirectory

                    if (attribute == FileAttribute.Directory)
                    {
                        StringBuilder dirName = new StringBuilder();
                        long CalculatedClaster = HexToDecimal(new byte[] { Data[i + 26], Data[i + 27], Data[i + 20], Data[i + 21] }); //кластер директории

                        dirName.Append(DecimalToASCII(Data, i + 1, i + 10));

                        if (Hex(Data[i + 0]).Contains("e5")) //удалённая директория.
                        {
                            dirName.Insert(0, 'Z'); //символ, которым мы заменяем пустое место.

                            if (!IsClasterFreeInFAT((int)CalculatedClaster)) //если кластер, на который указывает директория, затёрт, то её не восстановить.
                                continue;
                        }
                        else
                            dirName.Insert(0, DecimalToASCII(Data, i + 0, i + 0));

                        if (dirName.ToString().Contains("..")) //если это указатель на верхнюю директорию, то уходим.
                            continue;

                        DriveDirectory dir = new DriveDirectory(dirName.ToString());
                        directory.Directories.Add(dir);

                        ThreadPool.QueueUserWorkItem(ProcessDirectory, new DirectoryWithClaster(dir, CalculatedClaster));
                    }

                    #endregion

                    #region shortNameFile

                    if (attribute == FileAttribute.Archive)
                    {
                        StringBuilder fileName = new StringBuilder();

                        fileName.Append(DecimalToASCII(Data, i + 1, i + 10));

                        if (Hex(Data[i]).Contains("e5"))
                        {
                            fileName.Insert(0, 'Z');

                            DriveFile file = new DriveFile();

                            file.Name = fileName.ToString();

                            long fileSize = CalculateSize(Data, i + 28, i + 31);

                            if (fileSize > MAX_RECOVERING_FILE_SIZE) //не восстанавливаем файлы слишком большого размера.
                                continue;

                            //число кластеров, в которых расположен файл.
                            var NumberOfClastersForLocating = fileSize / ClasterSize;

                            if (fileSize % ClasterSize != 0)
                                NumberOfClastersForLocating++;

                            var startClasterNumber = HexToDecimal(new byte[] { Data[i + 26], Data[i + 27], Data[i + 20], Data[i + 21] });

                            //ищем свободные кластеры, начиная с того кластера, на который указывает файл.
                            for (int j = (int)startClasterNumber; NumberOfClastersForLocating > 0 && j < FAT.Length / 4 - 2; j++)
                                if (IsClasterFreeInFAT(j))
                                {
                                    file.Clasters.Add(j);
                                    NumberOfClastersForLocating--;
                                }

                            if (NumberOfClastersForLocating == 0)
                                directory.Files.Add(file);
                        }
                        else
                            continue; //нечего восстанавливать, если у файла нет пометки.
                    }

                    #endregion

                    #region LFN

                    if (attribute == FileAttribute.LFN)
                    {
                        int j = i;

                        bool isDeleted = false; //если это имя относится к файлу, то файл должен быть удалён.

                        if (Hex(Data[j + 0]).Contains("e5"))
                            isDeleted = true;

                        StringBuilder name = new StringBuilder(" ");

                        //собираем длинное имя.
                        while (GetAttribute(Data[j + 11]) == FileAttribute.LFN && j < Data.Length - 32)
                        {
                            name.Insert(0, DecimalToUnicode(Data, j + 28, j + 31));
                            name.Insert(0, DecimalToUnicode(Data, j + 14, j + 25));
                            name.Insert(0, DecimalToUnicode(Data, j + 1, j + 10));

                            j += 32;
                        }


                        if (GetAttribute(Data[j + 11]) == FileAttribute.Directory)
                        {
                            var CalculatedClaster = HexToDecimal(new byte[] { Data[j + 26], Data[j + 27], Data[j + 20], Data[j + 21] });

                            DriveDirectory dir = new DriveDirectory(name.ToString());

                            if (isDeleted)
                            {
                                if (IsClasterFreeInFAT((int)CalculatedClaster))
                                {
                                    directory.Directories.Add(dir);
                                    ThreadPool.QueueUserWorkItem(ProcessDirectory, new DirectoryWithClaster(dir, CalculatedClaster));
                                }
                            }
                            else
                            {
                                directory.Directories.Add(dir);
                                ThreadPool.QueueUserWorkItem(ProcessDirectory, new DirectoryWithClaster(dir, CalculatedClaster));
                            }
                        }

                        if (GetAttribute(Data[j + 11]) == FileAttribute.Archive && isDeleted)
                        {
                            DriveFile file = new DriveFile(name.ToString());

                            var StartClasterNumber = HexToDecimal(new byte[] { Data[j + 26], Data[j + 27], Data[j + 20], Data[j + 21] });

                            long fileSize = CalculateSize(Data, j + 28, j + 31);

                            //число кластеров, в которых расположен файл.
                            var NumberOfClastersForLocating = fileSize / ClasterSize;

                            if (fileSize % ClasterSize != 0)
                                NumberOfClastersForLocating++;

                            if (fileSize < MAX_RECOVERING_FILE_SIZE)
                            {
                                for (int k = (int)StartClasterNumber; NumberOfClastersForLocating > 0 && k < FAT.Length / 4 - 2; k++)
                                    if (IsClasterFreeInFAT(k))
                                    {
                                        file.Clasters.Add(k);
                                        NumberOfClastersForLocating--;
                                    }

                                if (NumberOfClastersForLocating == 0)
                                    directory.Files.Add(file);
                            }
                        }

                        i = j;
                    }

                    #endregion

                }
                catch (IndexOutOfRangeException e)
                {
                    System.Windows.Forms.MessageBox.Show(e.Message); // за границу обычно номер кластера.
                }
                catch (ContextMarshalException e)
                {
                    i = (int)ClasterSize; // если мы словили неверный атрибут, когда ходили по директории, то директория была затёрта.
                }
                catch (Exception e)
                {
                    System.Windows.Forms.MessageBox.Show(e.Message);
                    continue;
                }
            }
        }

        public MainWindow()
        {
            InitializeComponent();
            this.Closing += MainWindow_Closing;
        }

        private void MainWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            //освобождаем доступ к флешке.
            if (DiskHandle != null)
                DiskHandle.Close();
        }

        private void chooseDriveBtn_Click(object sender, RoutedEventArgs e)
        {
            byte[] Data;

            //освобождаем доступ к флешке.
            if (DiskHandle != null)
                DiskHandle.Close();

            StartOfFileData = 0; //сбрасываем счытанное начало файловой зоны

            #region maxRecoverySize

            try
            {
                MAX_RECOVERING_FILE_SIZE *= int.Parse(MaxRecoverySizeTB.Text);
            }
            catch
            {
                System.Windows.Forms.MessageBox.Show("Максимальный размер файла указан неверно. Установлено значение по умолчанию: 50 МБ.");
                MAX_RECOVERING_FILE_SIZE = 50 * 1024 * 1024;
            }

            #endregion

            try
            {
                FolderBrowserDialog folderDialog = new FolderBrowserDialog();

                if (folderDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    //обновляем букву выбранного диска.
                    tbName.Text = folderDialog.SelectedPath;

                    //приводим имя выбранной флешки к нормальному формату
                    var DiskName = folderDialog.SelectedPath.Split('\\')[0];

                    #region takingHandle

                    //забираем handle для доступа к диску
                    DiskHandle = CreateFile(
                lpFileName: @"\\.\" + DiskName,
                dwDesiredAccess: FileAccess.Read,
                dwShareMode: FileShare.ReadWrite,
                lpSecurityAttributes: IntPtr.Zero,
                dwCreationDisposition: FileMode.OpenOrCreate,
                dwFlagsAndAttributes: FileAttributes.Normal,
                hTemplateFile: IntPtr.Zero);

                    #endregion

                    //открываем поток на чтение
                    diskStreamToRead = new FileStream(DiskHandle, FileAccess.Read);

                    //читаем загрузочный сектор
                    Data = Read(0, BOOT_SECTOR_SIZE);

                    var FileSystemType = DecimalToASCII(Data,82,89);
                    tbFileSystem.Text = FileSystemType;

                    if (!FileSystemType.Contains("FAT32"))
                    {
                        System.Windows.Forms.MessageBox.Show("Программа работает только с системой FAT32");
                        return;
                    }

                    #region GetAndDisplaySpace

                    ulong OutFreeBytesAvaliable = 0;
                    ulong OutTotalNumberOfBytes = 0;
                    ulong OutTotalNumberOfFreeBytes = 0;


                    GetDiskFreeSpaceEx(
                        folderDialog.SelectedPath,
                        out OutFreeBytesAvaliable,
                        out OutTotalNumberOfBytes,
                        out OutTotalNumberOfFreeBytes);

                    tbTotal.Text = Math.Round(((double)OutTotalNumberOfBytes / 1024 / 1024 / 1024), 2) + " Гб";
                    tbFree.Text = Math.Round(((double)OutTotalNumberOfFreeBytes / 1024 / 1024 / 1024), 2) + " Гб";

                    #endregion

                    #region readOptions

                    long SectorSize = HexToDecimal(Data,11,12);
                    long NumberOfSectorsInClaster = HexToDecimal(Data,13,13);
                    long NumberOfReservedSectors = HexToDecimal(Data,14,15);
                    long NumberOfFatCopies = HexToDecimal(Data,16,16);
                    long FatTableSizeInSectors = HexToDecimal(Data,36,39);
                    long SectorsInFileSystem = HexToDecimal(Data,32,35);

                    #endregion

                    ClasterSize = NumberOfSectorsInClaster * SectorSize;
                    //смещение к корневой директории.
                    StartOfFileData = NumberOfReservedSectors * SectorSize + FatTableSizeInSectors * SectorSize * NumberOfFatCopies;

                    var offsetToFAT = NumberOfReservedSectors * SectorSize;

                    FAT = ReadFAT(offsetToFAT, (int)(FatTableSizeInSectors * SectorSize));

                    System.Windows.Forms.MessageBox.Show("Начато сканирование устройства.");

                    DriveDirectory root = new DriveDirectory("recovered");

                    ProcessDirectory(new DirectoryWithClaster(root, 2));

                    System.Windows.Forms.MessageBox.Show("Сканирование завершено.");

                    rootRecover = root;

                    //тут нужно всё собрать.
                }

            }
            catch (Exception ex)
            {
                System.Windows.Forms.MessageBox.Show(ex.Message);
            }

        }

        #region CreateFile

        //возвращает handle к диску.
        [DllImport("Kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        public static extern SafeFileHandle CreateFile(
            string lpFileName,
            [MarshalAs(UnmanagedType.U4)] FileAccess dwDesiredAccess,
            [MarshalAs(UnmanagedType.U4)] FileShare dwShareMode,
            IntPtr lpSecurityAttributes,
            [MarshalAs(UnmanagedType.U4)] FileMode dwCreationDisposition,
            [MarshalAs(UnmanagedType.U4)] FileAttributes dwFlagsAndAttributes,
            IntPtr hTemplateFile);

        #endregion

        #region GetDiskFreeSpaceEx
        //метод для того, чтобы взять информацию о свободном месте на диске.
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool GetDiskFreeSpaceEx(
            string lpDirectoryName,
           out ulong lpFreeBytesAvailable,
           out ulong lpTotalNumberOfBytes,
           out ulong lpTotalNumberOfFreeBytes);

        #endregion

        private void MaxRecoverySizeTB_PreviewTextInput(object sender, System.Windows.Input.TextCompositionEventArgs e)
        {
            if (!Char.IsDigit(e.Text[0])) //нечего тут всякие буквы в цифровое поле вводить.
                e.Handled = true;
        }

        private void ChangeRecoveryDirBtn_Click(object sender, RoutedEventArgs e)
        {
            FolderBrowserDialog fdb = new FolderBrowserDialog();

            if(fdb.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                RECOVERY_DIRECTORY = fdb.SelectedPath;
                RecoveryDirTB.Text = RECOVERY_DIRECTORY;
            }
        }

        //тут начинается сборка файлов из г*** и палок. В смысле, из того, что было прочитано в файловой системе.
        private void StartRecovery_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                CreateFolderWithFiles(RECOVERY_DIRECTORY + "\\", rootRecover);

                System.Diagnostics.Process.Start("explorer", RECOVERY_DIRECTORY);
            }
            catch(Exception ex)
            {
                System.Windows.Forms.MessageBox.Show(ex.Message);
            }
        }
    }

    enum FileAttribute
    {
        ReadOnly, Hidden, SystemFile, Drive, LFN, Directory, Archive, Empty, SystemRegistry, StrangeAttribute
    }
}
