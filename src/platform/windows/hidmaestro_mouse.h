/**
 * @file src/platform/windows/hidmaestro_mouse.h
 * @brief Testable conversions used by the HIDMaestro Windows mouse adapter.
 */
#pragma once

#include <cstdint>
#include <vector>

namespace platf::hidmaestro {
  /**
   * @brief Normalize one pixel coordinate to the HID absolute range.
   *
   * @param position Pixel coordinate.
   * @param dimension Full axis dimension in pixels.
   * @return Clamped coordinate from 0 through 32767.
   */
  int normalize_absolute(float position, int dimension);

  /**
   * @brief Convert a Moonlight mouse button identifier to HIDMaestro ordering.
   *
   * @param button Moonlight button identifier.
   * @return HIDMaestro button from 1 through 5, or zero when unsupported.
   */
  std::uint32_t translate_button(int button);

  /**
   * @brief Convert high-resolution wheel input into bounded HID detent chunks.
   *
   * @param remainder Pending high-resolution units from earlier calls.
   * @param distance New high-resolution wheel distance.
   * @return Signed detent chunks, each bounded to the HID range.
   */
  std::vector<int> scroll_chunks(int &remainder, int distance);
}  // namespace platf::hidmaestro
