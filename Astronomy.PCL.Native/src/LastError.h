#ifndef ASTRONOMY_PCL_LAST_ERROR_H
#define ASTRONOMY_PCL_LAST_ERROR_H

#include <pcl/String.h>

namespace astronomy
{
    void SetLastError(const pcl::String& message);
    const pcl::String& GetLastError() noexcept;
    void ClearLastError() noexcept;
}

#endif
