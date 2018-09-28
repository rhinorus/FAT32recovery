using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FilesUndelitingFAT32
{
    class DriveDirectory
    {
        private string name;
        public string Name
        {
            get { return name; }

            set //очищаем имя от лишних Юникодовских символов.
            {
                StringBuilder sb = new StringBuilder();

                if (value.Contains('\0'))
                    sb.Append(value.Split('\0')[0]);    
                else
                    sb.Append(value);

                while (sb[sb.Length - 1] == ' ')
                    sb.Remove(sb.Length - 1, 1);

                name = sb.ToString();
            }
        }
        public List<DriveDirectory> Directories { get; set; }
        public List<DriveFile> Files { get; set; }

        public DriveDirectory()
        {
            Directories = new List<DriveDirectory>();
            Files = new List<DriveFile>();

            Name = "no-name";
        }

        public DriveDirectory(string name)
        {
            Name = name;

            Directories = new List<DriveDirectory>();
            Files = new List<DriveFile>();
        }
    }
}
