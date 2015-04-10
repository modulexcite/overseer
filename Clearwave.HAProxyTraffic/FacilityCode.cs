using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Clearwave.HAProxyTraffic
{
    public enum FacilityCode
    {
        None = -1,
        KernelMessage = 0,
        UserLevelMessage = 1,
        MailSystem = 2,
        System = 3,
        SecurityAuthMessage = 4,
        InternalSyslogGeneratedMessage = 5,
        LinePrinter = 6,
        NetworkNews = 7,
        UUCP = 8,
        Clock = 9,
        SecurityAuthMessage2 = 10,
        FTP = 11,
        NTP = 12,
        LogAudit = 13,
        LogAlert = 14,
        Clock2 = 15,
        LocalUse0 = 16,
        LocalUse1 = 17,
        LocalUse2 = 18,
        LocalUse3 = 19,
        LocalUse4 = 20,
        LocalUse5 = 21,
        LocalUse6 = 22,
        LocalUse7 = 23
    }
}
