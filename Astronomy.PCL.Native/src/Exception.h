#ifndef ASTRONOMY_PCL_EXCEPTION_H
#define ASTRONOMY_PCL_EXCEPTION_H

#include <exception>
#include <typeinfo>
#include <pcl/Exception.h>
#include "LastError.h"
#include "Astronomy/PCL/XisfCApi.h"

#define ASTRONOMY_PCL_TRY    try {

#define ASTRONOMY_PCL_CATCH                                                                                   \
    } catch (const pcl::Exception& e) {                                                                       \
        ::astronomy::SetLastError(e.Message());                                                               \
        return AstronomyXisfStatus_PclException;                                                              \
    } catch (const std::exception& e) {                                                                       \
        ::astronomy::SetLastError(pcl::String(e.what()));                                                     \
        return AstronomyXisfStatus_StdException;                                                              \
    } catch (...) {                                                                                           \
        ::astronomy::SetLastError(pcl::String("Unknown C++ exception"));                                      \
        return AstronomyXisfStatus_UnknownException;                                                          \
    }

#endif
