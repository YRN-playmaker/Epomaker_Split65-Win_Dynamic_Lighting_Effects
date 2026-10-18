// SPDX-License-Identifier: GPL-2.0-or-later
// UMDF2 VHF source. Physical HID/radio I/O is confined to the user client.
#define WIN32_NO_STATUS
#include <windows.h>
#undef WIN32_NO_STATUS
#include <wudfwdm.h>
#include <wdf.h>
#include <vhf.h>
#include "../core/bridge_core.h"

static const GUID InterfaceGuid = {0x36b26c21,0x62c2,0x4eb9,{0xb8,0x6b,0xb6,0xed,0x44,0xaf,0x6b,0xa1}};
static const GUID ContainerGuid = {0xf4e47db9,0xd616,0x4911,{0x8b,0xec,0x1d,0xa2,0xa6,0xe1,0xbe,0xb9}};
#define IOCTL_SB_ATTACH CTL_CODE(FILE_DEVICE_UNKNOWN,0x800,METHOD_BUFFERED,FILE_READ_DATA|FILE_WRITE_DATA)
#define IOCTL_SB_SNAPSHOT CTL_CODE(FILE_DEVICE_UNKNOWN,0x801,METHOD_BUFFERED,FILE_READ_DATA|FILE_WRITE_DATA)
#define IOCTL_SB_PROBE CTL_CODE(FILE_DEVICE_UNKNOWN,0x802,METHOD_BUFFERED,FILE_READ_DATA|FILE_WRITE_DATA)
static UCHAR ReportDescriptor[] = {
#include "../../dynamic_lighting/descriptor.inc"
};
typedef struct {
    WDFDEVICE device;
    WDFIOTARGET localTarget;
    WDFWAITLOCK sessionLock;
    SRWLOCK stateLock;
    BOOLEAN prepared;
    WDFFILEOBJECT owner;
    VHFHANDLE vhf;
    sb_state_t state;
} DEVICE_CONTEXT;
WDF_DECLARE_CONTEXT_TYPE_WITH_NAME(DEVICE_CONTEXT, GetContext)
DRIVER_INITIALIZE DriverEntry;
EVT_WDF_DRIVER_DEVICE_ADD DeviceAdd;
EVT_WDF_IO_QUEUE_IO_DEVICE_CONTROL DeviceControl;
EVT_WDF_FILE_CLEANUP FileCleanup;
EVT_WDF_OBJECT_CONTEXT_CLEANUP DeviceCleanup;
EVT_WDF_DEVICE_PREPARE_HARDWARE PrepareHardware;
EVT_WDF_DEVICE_RELEASE_HARDWARE ReleaseHardware;
EVT_VHF_ASYNC_OPERATION GetFeature;
EVT_VHF_ASYNC_OPERATION SetFeature;

static VOID Stage(PCWSTR message, NTSTATUS status) {
    WCHAR value[] = L"NTSTATUS=0x00000000";
    LPCWSTR strings[2] = { message, value };
    HANDLE source;
    // Low-volume startup evidence; never records keys or RGB payloads.
    for (unsigned i = 0; i < 8; ++i)
        value[11 + i] = L"0123456789ABCDEF"[((ULONG)status >> (28 - 4 * i)) & 15];
    OutputDebugStringW(message);
    source = RegisterEventSourceW(NULL, L"Split65LampBridge");
    if (source) {
        ReportEventW(source, NT_SUCCESS(status) ? EVENTLOG_INFORMATION_TYPE : EVENTLOG_ERROR_TYPE,
                     0, 2001, NULL, 2, 0, strings, NULL);
        DeregisterEventSource(source);
    }
}

VOID GetFeature(PVOID opaque, VHFOPERATIONHANDLE operation, PVOID operationContext, PHID_XFER_PACKET packet) {
    DEVICE_CONTEXT *ctx = opaque;
    size_t written = 0;
    UNREFERENCED_PARAMETER(operationContext);
    AcquireSRWLockExclusive(&ctx->stateLock);
    BOOLEAN ok = sb_get_feature(&ctx->state, packet->reportId, packet->reportBuffer,
                               packet->reportBufferLen, &written);
    ReleaseSRWLockExclusive(&ctx->stateLock);
    if (ok) packet->reportBufferLen = (ULONG)written;
    (void)VhfAsyncOperationComplete(operation, ok ? STATUS_SUCCESS : STATUS_INVALID_PARAMETER);
}
VOID SetFeature(PVOID opaque, VHFOPERATIONHANDLE operation, PVOID operationContext, PHID_XFER_PACKET packet) {
    DEVICE_CONTEXT *ctx = opaque;
    UNREFERENCED_PARAMETER(operationContext);
    AcquireSRWLockExclusive(&ctx->stateLock);
    BOOLEAN ok = sb_set_feature(&ctx->state, packet->reportId, packet->reportBuffer, packet->reportBufferLen);
    ReleaseSRWLockExclusive(&ctx->stateLock);
    (void)VhfAsyncOperationComplete(operation, ok ? STATUS_SUCCESS : STATUS_INVALID_PARAMETER);
}
static VOID StopVirtualDevice(DEVICE_CONTEXT *ctx) {
    // Called at PASSIVE_LEVEL under sessionLock; callbacks never take that lock.
    if (ctx->vhf) { VhfDelete(ctx->vhf, TRUE); ctx->vhf = NULL; }
    AcquireSRWLockExclusive(&ctx->stateLock);
    sb_attach(&ctx->state, false);
    ReleaseSRWLockExclusive(&ctx->stateLock);
    ctx->owner = NULL;
}
static NTSTATUS StartVirtualDevice(DEVICE_CONTEXT *ctx) {
    VHF_CONFIG config;
    if (!ctx->prepared || !ctx->localTarget) return STATUS_DEVICE_NOT_READY;
    HANDLE file = WdfIoTargetWdmGetTargetFileHandle(ctx->localTarget);
    if (!file || file == INVALID_HANDLE_VALUE) return STATUS_INVALID_HANDLE;
    VHF_CONFIG_INIT(&config, file, (USHORT)sizeof(ReportDescriptor), ReportDescriptor);
    config.VhfClientContext = ctx;
    // Temporary experimental identity for this project's virtual lighting only.
    config.VendorID = 0x342D; config.ProductID = 0xE4C8; config.VersionNumber = 0x0050;
    config.ContainerID = ContainerGuid;
    config.EvtVhfAsyncOperationGetFeature = GetFeature;
    config.EvtVhfAsyncOperationSetFeature = SetFeature;
    AcquireSRWLockExclusive(&ctx->stateLock);
    sb_attach(&ctx->state, true);
    ReleaseSRWLockExclusive(&ctx->stateLock);
    NTSTATUS status = VhfCreate(&config, &ctx->vhf);
    if (NT_SUCCESS(status)) status = VhfStart(ctx->vhf);
    if (!NT_SUCCESS(status)) StopVirtualDevice(ctx);
    Stage(L"Split65 0.5.0 VHF create/start", status);
    return status;
}
VOID DeviceControl(WDFQUEUE queue, WDFREQUEST request, size_t outputLength, size_t inputLength, ULONG code) {
    DEVICE_CONTEXT *ctx = GetContext(WdfIoQueueGetDevice(queue));
    WDFFILEOBJECT file = WdfRequestGetFileObject(request);
    NTSTATUS status = STATUS_INVALID_DEVICE_REQUEST;
    size_t returned = 0;
    UNREFERENCED_PARAMETER(inputLength); UNREFERENCED_PARAMETER(outputLength);
    if (code != IOCTL_SB_ATTACH && code != IOCTL_SB_SNAPSHOT && code != IOCTL_SB_PROBE) {
        WDF_REQUEST_SEND_OPTIONS options;
        WDF_REQUEST_SEND_OPTIONS_INIT(&options, WDF_REQUEST_SEND_OPTION_SEND_AND_FORGET);
        WdfRequestFormatRequestUsingCurrentType(request);
        if (!WdfRequestSend(request, WdfDeviceGetIoTarget(ctx->device), &options))
            WdfRequestComplete(request, WdfRequestGetStatus(request));
        return;
    }
    WdfWaitLockAcquire(ctx->sessionLock, NULL);
    if (!ctx->prepared) status = STATUS_DEVICE_NOT_READY;
    else if (code == IOCTL_SB_PROBE) {
        static const UCHAR reply[16] = {'S','L','B','P',0,5,0,1,SB_SNAPSHOT_SIZE,0,0,0,1,0,0,0};
        UCHAR *buffer;
        status = WdfRequestRetrieveOutputBuffer(request, sizeof(reply), (PVOID *)&buffer, NULL);
        if (NT_SUCCESS(status)) { memcpy(buffer, reply, sizeof(reply)); returned = sizeof(reply); }
    }
    else if (!file) status = STATUS_INVALID_HANDLE;
    else if (ctx->owner && ctx->owner != file) status = STATUS_SHARING_VIOLATION;
    else if (code == IOCTL_SB_ATTACH) {
        ULONG *value;
        status = WdfRequestRetrieveInputBuffer(request, sizeof(*value), (PVOID *)&value, NULL);
        if (NT_SUCCESS(status)) {
            if (*value > 1) status = STATUS_INVALID_PARAMETER;
            else if (!*value) { StopVirtualDevice(ctx); status = STATUS_SUCCESS; }
            else if (!ctx->vhf) { ctx->owner = file; status = StartVirtualDevice(ctx); }
            else status = STATUS_SUCCESS;
        }
    } else if (code == IOCTL_SB_SNAPSHOT) {
        UCHAR *buffer;
        if (ctx->owner != file || !ctx->vhf) status = STATUS_DEVICE_NOT_READY;
        else {
            status = WdfRequestRetrieveOutputBuffer(request, SB_SNAPSHOT_SIZE, (PVOID *)&buffer, NULL);
            if (NT_SUCCESS(status)) {
                AcquireSRWLockExclusive(&ctx->stateLock);
                sb_snapshot(&ctx->state, buffer);
                ReleaseSRWLockExclusive(&ctx->stateLock);
                returned = SB_SNAPSHOT_SIZE;
            }
        }
    }
    WdfWaitLockRelease(ctx->sessionLock);
    WdfRequestCompleteWithInformation(request, status, returned);
}
VOID FileCleanup(WDFFILEOBJECT file) {
    DEVICE_CONTEXT *ctx = GetContext(WdfFileObjectGetDevice(file));
    WdfWaitLockAcquire(ctx->sessionLock, NULL);
    if (ctx->owner == file) StopVirtualDevice(ctx);
    WdfWaitLockRelease(ctx->sessionLock);
}
VOID DeviceCleanup(WDFOBJECT object) {
    DEVICE_CONTEXT *ctx = GetContext(object);
    if (ctx->sessionLock) {
        WdfWaitLockAcquire(ctx->sessionLock, NULL);
        StopVirtualDevice(ctx);
        WdfWaitLockRelease(ctx->sessionLock);
    }
}

NTSTATUS PrepareHardware(WDFDEVICE device, WDFCMRESLIST raw, WDFCMRESLIST translated) {
    DEVICE_CONTEXT *ctx = GetContext(device);
    WDF_OBJECT_ATTRIBUTES attributes;
    WDF_IO_TARGET_OPEN_PARAMS open;
    NTSTATUS status;
    UNREFERENCED_PARAMETER(raw); UNREFERENCED_PARAMETER(translated);
    // Match Microsoft's DMF VirtualHidDeviceVhf user-mode implementation:
    // the lower VHF stack must be started before opening its local file.
    WDF_OBJECT_ATTRIBUTES_INIT(&attributes); attributes.ParentObject = device;
    status = WdfIoTargetCreate(device, &attributes, &ctx->localTarget);
    if (NT_SUCCESS(status)) {
        WDF_IO_TARGET_OPEN_PARAMS_INIT_OPEN_BY_FILE(&open, NULL);
        status = WdfIoTargetOpen(ctx->localTarget, &open);
    }
    if (!NT_SUCCESS(status) && ctx->localTarget) {
        WdfObjectDelete(ctx->localTarget); ctx->localTarget = NULL;
    }
    ctx->prepared = NT_SUCCESS(status);
    Stage(L"Split65 0.5.0 PrepareHardware/open VHF", status);
    return status;
}
NTSTATUS ReleaseHardware(WDFDEVICE device, WDFCMRESLIST translated) {
    DEVICE_CONTEXT *ctx = GetContext(device);
    WDFIOTARGET target;
    UNREFERENCED_PARAMETER(translated);
    WdfWaitLockAcquire(ctx->sessionLock, NULL);
    ctx->prepared = FALSE;
    StopVirtualDevice(ctx);
    target = ctx->localTarget; ctx->localTarget = NULL;
    WdfWaitLockRelease(ctx->sessionLock);
    // Closing can cause file callbacks; do not hold sessionLock here.
    if (target) { WdfIoTargetClose(target); WdfObjectDelete(target); }
    Stage(L"Split65 0.5.0 ReleaseHardware", STATUS_SUCCESS);
    return STATUS_SUCCESS;
}
NTSTATUS DeviceAdd(WDFDRIVER driver, PWDFDEVICE_INIT init) {
    WDFDEVICE device;
    WDF_OBJECT_ATTRIBUTES attributes, childAttributes;
    WDF_FILEOBJECT_CONFIG files;
    WDF_IO_QUEUE_CONFIG queue;
    WDF_PNPPOWER_EVENT_CALLBACKS pnp;
    NTSTATUS status;
    UNREFERENCED_PARAMETER(driver);
    // The INF places WUDFRd above the inbox VHF function driver.
    WdfFdoInitSetFilter(init);
    WDF_PNPPOWER_EVENT_CALLBACKS_INIT(&pnp);
    pnp.EvtDevicePrepareHardware = PrepareHardware;
    pnp.EvtDeviceReleaseHardware = ReleaseHardware;
    WdfDeviceInitSetPnpPowerEventCallbacks(init, &pnp);
    WDF_FILEOBJECT_CONFIG_INIT(&files, WDF_NO_EVENT_CALLBACK, WDF_NO_EVENT_CALLBACK, FileCleanup);
    files.FileObjectClass = WdfFileObjectWdfCannotUseFsContexts;
    WDF_OBJECT_ATTRIBUTES_INIT(&attributes);
    attributes.ExecutionLevel = WdfExecutionLevelPassive;
    WdfDeviceInitSetFileObjectConfig(init, &files, &attributes);
    WDF_OBJECT_ATTRIBUTES_INIT_CONTEXT_TYPE(&attributes, DEVICE_CONTEXT);
    attributes.ExecutionLevel = WdfExecutionLevelPassive;
    attributes.EvtCleanupCallback = DeviceCleanup;
    status = WdfDeviceCreate(&init, &attributes, &device);
    if (!NT_SUCCESS(status)) return status;
    DEVICE_CONTEXT *ctx = GetContext(device);
    ctx->device = device; sb_init(&ctx->state);
    InitializeSRWLock(&ctx->stateLock);
    WDF_OBJECT_ATTRIBUTES_INIT(&childAttributes); childAttributes.ParentObject = device;
    status = WdfWaitLockCreate(&childAttributes, &ctx->sessionLock);
    if (!NT_SUCCESS(status)) return status;
    status = WdfDeviceCreateDeviceInterface(device, &InterfaceGuid, NULL);
    if (!NT_SUCCESS(status)) return status;
    WDF_IO_QUEUE_CONFIG_INIT_DEFAULT_QUEUE(&queue, WdfIoQueueDispatchSequential);
    queue.PowerManaged = WdfFalse;
    queue.EvtIoDeviceControl = DeviceControl;
    status = WdfIoQueueCreate(device, &queue, WDF_NO_OBJECT_ATTRIBUTES, WDF_NO_HANDLE);
    Stage(L"Split65 0.5.0 DeviceAdd", status);
    return status;
}
NTSTATUS DriverEntry(PDRIVER_OBJECT driver, PUNICODE_STRING registryPath) {
    WDF_DRIVER_CONFIG config;
    WDF_DRIVER_CONFIG_INIT(&config, DeviceAdd);
    return WdfDriverCreate(driver, registryPath, WDF_NO_OBJECT_ATTRIBUTES, &config, WDF_NO_HANDLE);
}
