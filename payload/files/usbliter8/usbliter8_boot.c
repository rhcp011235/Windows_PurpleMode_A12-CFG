/*
 * usbliter8_boot.c
 *
 * Minimal C port of the "boot" command from usbliter8ctl (Python/pyusb).
 * Sends a raw image (PongoOS / iBoot payload) to a device that is already
 * in "Pwned DFU" mode and triggers execution.
 *
 * This tool does NOT perform the SecureROM exploit itself -- it assumes
 * the device already reports "PWND:[" in its DFU serial string (i.e. the
 * checkm8-class exploit has already run through whatever tool put it
 * there). It only implements the post-exploit control channel:
 *
 *   1. Find the DFU device (05ac:1227)
 *   2. Verify serial contains "PWND:["
 *   3. DFU_DNLOAD the image in 0x800-byte chunks
 *   4. Send a zero-length DFU_DNLOAD to close the transfer
 *   5. Send CUSTOM_BOOT (bRequest 8) to jump to the payload
 *   6. Best-effort DFU_ABORT cleanup
 *
 * Build: see Makefile (requires libusb-1.0).
 */

#include <stdio.h>
#include <stdlib.h>
#include <string.h>

#include <libusb.h>

#define DFU_VID          0x05AC
#define DFU_PID          0x1227

#define DFU_DNLOAD       1
#define DFU_ABORT        6
#define CUSTOM_BOOT      8

#define TRANSFER_SIZE    0x800u
#define CTRL_TIMEOUT_MS  5000

/* bmRequestType 0x21 = host->device | class | interface */
#define REQTYPE_CLASS_OUT (LIBUSB_REQUEST_TYPE_CLASS | LIBUSB_RECIPIENT_INTERFACE | LIBUSB_ENDPOINT_OUT)

static libusb_device_handle *open_pwnd_device(void)
{
    fprintf(stderr, "[boot] opening USB device 05ac:1227\n");
    libusb_device_handle *handle = libusb_open_device_with_vid_pid(NULL, DFU_VID, DFU_PID);
    if (!handle) {
        fprintf(stderr, "error: no DFU device found (%04x:%04x). "
                        "Is the device plugged in and in DFU mode?\n", DFU_VID, DFU_PID);
        return NULL;
    }

    /* harmless no-op on platforms that don't support kernel driver detach */
    int detach_result = libusb_set_auto_detach_kernel_driver(handle, 1);
    fprintf(stderr, "[boot] auto-detach result: %d (%s)\n", detach_result, libusb_error_name(detach_result));

    libusb_device *dev = libusb_get_device(handle);
    struct libusb_device_descriptor desc;
    unsigned char serial[256] = {0};

    if (libusb_get_device_descriptor(dev, &desc) == 0 && desc.iSerialNumber) {
        libusb_get_string_descriptor_ascii(handle, desc.iSerialNumber, serial, sizeof(serial) - 1);
    }

    if (!strstr((const char *)serial, "PWND:[")) {
        fprintf(stderr, "error: device is not in Pwned DFU mode\n");
        if (serial[0]) fprintf(stderr, "serial: %s\n", serial);
        libusb_close(handle);
        return NULL;
    }

    printf("Serial: %s\n", serial);
    return handle;
}

static int download_image(libusb_device_handle *h, const unsigned char *buf, size_t len)
{
    size_t offset = 0;
    while (offset < len) {
        size_t chunk = (len - offset < TRANSFER_SIZE) ? (len - offset) : TRANSFER_SIZE;
        int r = libusb_control_transfer(h, REQTYPE_CLASS_OUT, DFU_DNLOAD, 0, 0,
                                         (unsigned char *)(buf + offset), (uint16_t)chunk,
                                         CTRL_TIMEOUT_MS);
        if (r < 0) {
            fprintf(stderr, "\nerror: DFU_DNLOAD failed at offset 0x%zx: %s\n",
                    offset, libusb_error_name(r));
            return -1;
        }
        offset += chunk;
        printf("\rsent 0x%zx / 0x%zx", offset, len);
        fflush(stdout);
    }
    printf("\n");

    /* zero-length DFU_DNLOAD closes the transfer */
    int r = libusb_control_transfer(h, REQTYPE_CLASS_OUT, DFU_DNLOAD, 0, 0, NULL, 0, 1000);
    if (r < 0) {
        fprintf(stderr, "warning: closing DFU_DNLOAD failed: %s\n", libusb_error_name(r));
    }
    return 0;
}

int main(int argc, char **argv)
{
    setvbuf(stdout, NULL, _IONBF, 0);
    setvbuf(stderr, NULL, _IONBF, 0);
    const struct libusb_version *usb_version = libusb_get_version();
    fprintf(stderr, "[boot] USBLiter8 boot starting; libusb %d.%d.%d.%d\n",
            usb_version->major, usb_version->minor, usb_version->micro, usb_version->nano);
    if (argc != 2) {
        fprintf(stderr, "usage: %s <image.bin>\n", argv[0]);
        return 1;
    }

    FILE *f = fopen(argv[1], "rb");
    if (!f) { perror("fopen"); return 1; }

    fseek(f, 0, SEEK_END);
    long size = ftell(f);
    fseek(f, 0, SEEK_SET);
    if (size <= 0) { fprintf(stderr, "error: empty or unreadable file\n"); fclose(f); return 1; }

    unsigned char *buf = malloc((size_t)size);
    if (!buf) { fclose(f); fprintf(stderr, "error: out of memory\n"); return 1; }

    if (fread(buf, 1, (size_t)size, f) != (size_t)size) {
        fprintf(stderr, "error: short read on %s\n", argv[1]);
        fclose(f); free(buf); return 1;
    }
    fclose(f);

    printf("Image: %s (%ld bytes)\n", argv[1], size);

#ifdef LIBUSB_OPTION_USE_USBDK
    libusb_set_option(NULL, LIBUSB_OPTION_USE_USBDK);
#endif

    fprintf(stderr, "[boot] initializing libusb\n");
    int r = libusb_init(NULL);
    if (r < 0) {
        fprintf(stderr, "error: libusb_init failed: %s\n", libusb_error_name(r));
        free(buf);
        return 1;
    }

    fprintf(stderr, "[boot] libusb initialized\n");
    libusb_device_handle *handle = open_pwnd_device();
    if (!handle) { libusb_exit(NULL); free(buf); return 1; }

    fprintf(stderr, "[boot] claiming interface 0\n");
    r = libusb_claim_interface(handle, 0);
    if (r < 0) {
        fprintf(stderr, "error: could not claim interface 0: %s\n", libusb_error_name(r));
        libusb_close(handle);
        libusb_exit(NULL);
        free(buf);
        return 1;
    }

    fprintf(stderr, "[boot] interface claimed; sending image\n");
    int rc = download_image(handle, buf, (size_t)size);
    if (rc == 0) {
        fprintf(stderr, "[boot] image sent; triggering CUSTOM_BOOT\n");
        r = libusb_control_transfer(handle, REQTYPE_CLASS_OUT, CUSTOM_BOOT, 0, 0, NULL, 0, 1000);
        if (r < 0) {
            fprintf(stderr, "error: CUSTOM_BOOT failed: %s\n", libusb_error_name(r));
            rc = -1;
        } else {
            printf("Boot triggered\n");
        }
        /* Device normally disappears immediately after CUSTOM_BOOT. Do not issue
           another transfer against a handle that may already be invalid. */
    }

    libusb_release_interface(handle, 0);
    libusb_close(handle);
    libusb_exit(NULL);
    free(buf);

    return rc == 0 ? 0 : 1;
}
