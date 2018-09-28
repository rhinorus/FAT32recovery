using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FilesUndelitingFAT32
{
    class DirectoryWithClaster
    {
        public DriveDirectory Directory { get; set; }
        public long Claster { get; set;}

        public DirectoryWithClaster(DriveDirectory directory, long claster)
        {
            Directory = directory;
            Claster = claster;
        }
    }
}
