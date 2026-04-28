#include "LastError.h"

namespace astronomy
{
    namespace
    {
        thread_local pcl::String g_lastError;
    }

    void SetLastError(const pcl::String& message)
    {
        g_lastError = message;
    }

    const pcl::String& GetLastError() noexcept
    {
        return g_lastError;
    }

    void ClearLastError() noexcept
    {
        g_lastError.Clear();
    }
}
