#ifndef ASTRONOMY_PCL_SILENT_LOG_HANDLER_H
#define ASTRONOMY_PCL_SILENT_LOG_HANDLER_H

#include <pcl/XISF.h>

namespace astronomy
{
    class SilentLogHandler : public pcl::XISFLogHandler
    {
    public:
        void Init(const pcl::String&, bool) override {}
        void Log(const pcl::String&, message_type) override {}
        void Close() override {}
    };
}

#endif
