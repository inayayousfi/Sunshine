#ifndef HIDMAESTRO_MOUSE_H
#define HIDMAESTRO_MOUSE_H

#include <stdint.h>

#ifdef _WIN32
#define HIDMAESTRO_MOUSE_API __declspec(dllimport)
#else
#define HIDMAESTRO_MOUSE_API
#endif

#ifdef __cplusplus
extern "C" {
#endif

typedef void *hidmaestro_mouse_handle;

enum hidmaestro_mouse_button {
    HIDMAESTRO_MOUSE_BUTTON_LEFT = 1,
    HIDMAESTRO_MOUSE_BUTTON_RIGHT = 2,
    HIDMAESTRO_MOUSE_BUTTON_MIDDLE = 3,
    HIDMAESTRO_MOUSE_BUTTON_BACK = 4,
    HIDMAESTRO_MOUSE_BUTTON_FORWARD = 5,
};

HIDMAESTRO_MOUSE_API hidmaestro_mouse_handle __cdecl hidmaestro_mouse_create(void);
HIDMAESTRO_MOUSE_API int __cdecl hidmaestro_mouse_destroy(hidmaestro_mouse_handle handle);
HIDMAESTRO_MOUSE_API int __cdecl hidmaestro_mouse_relative(hidmaestro_mouse_handle handle, int32_t x, int32_t y);
HIDMAESTRO_MOUSE_API int __cdecl hidmaestro_mouse_absolute(hidmaestro_mouse_handle handle, int32_t x, int32_t y);
HIDMAESTRO_MOUSE_API int __cdecl hidmaestro_mouse_button(hidmaestro_mouse_handle handle, uint32_t button, int pressed);
HIDMAESTRO_MOUSE_API int __cdecl hidmaestro_mouse_scroll(hidmaestro_mouse_handle handle, int32_t vertical, int32_t horizontal);
HIDMAESTRO_MOUSE_API const char *__cdecl hidmaestro_mouse_last_error(void);

#ifdef __cplusplus
}
#endif

#endif
