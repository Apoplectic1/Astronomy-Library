#ifndef ASTRONOMY_PCL_LAST_ERROR_H
#define ASTRONOMY_PCL_LAST_ERROR_H

#pragma warning(push)
#pragma warning(disable: 4100 4456 4457 4458)
#include <pcl/String.h>
#pragma warning(pop)

namespace astronomy
{
    void SetLastError(const pcl::String& message);
    const pcl::String& GetLastError() noexcept;
    void ClearLastError() noexcept;
}

#endif
