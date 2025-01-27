#include "pch.h"

#include "Jni.h"

#include <jni.h>

#undef JNIEXPORT
#define JNIEXPORT __attribute__((visibility("default")))

#include "sdk/android/native_api/base/init.h"
#include "sdk/android/src/jni/class_reference_holder.h"
#include "sdk/android/src/jni/jni_helpers.h"

using namespace ::webrtc;

namespace unity 
{
namespace webrtc 
{

static JavaVM* g_jvm = nullptr;

extern "C" jint JNIEXPORT JNICALL JNI_OnLoad(JavaVM* jvm, void* reserved)
{
    InitAndroid(jvm);
    g_jvm = jvm;

    JNIEnv* jni = nullptr;
    if (jvm->GetEnv(reinterpret_cast<void**>(&jni), JNI_VERSION_1_6) != JNI_OK)
        return -1;

    return JNI_VERSION_1_6;
}

extern "C" void JNIEXPORT JNICALL JNI_OnUnLoad(JavaVM* jvm, void* reserved)
{
    g_jvm = nullptr;
}

bool IsVMInitialized()
{
    return g_jvm != nullptr;
}

}
}