#ifndef ASTRONOMY_PCL_SILENT_LOG_HANDLER_H
#define ASTRONOMY_PCL_SILENT_LOG_HANDLER_H

#pragma warning(push)
#pragma warning(disable: 4100 4456 4457 4458)
#include <pcl/XISF.h>
#pragma warning(pop)

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
