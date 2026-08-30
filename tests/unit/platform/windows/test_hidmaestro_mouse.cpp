/**
 * @file tests/unit/platform/windows/test_hidmaestro_mouse.cpp
 * @brief Tests for HIDMaestro mouse input conversions.
 */

#ifdef _WIN32

  #include "src/platform/common.h"
  #include "src/platform/windows/hidmaestro_mouse.h"

  #include <gtest/gtest.h>

TEST(HidMaestroMouseTest, NormalizesAbsoluteCoordinates) {
  EXPECT_EQ(platf::hidmaestro::normalize_absolute(-10.0F, 1920), 0);
  EXPECT_EQ(platf::hidmaestro::normalize_absolute(0.0F, 1920), 0);
  EXPECT_EQ(platf::hidmaestro::normalize_absolute(1919.0F, 1920), 32767);
  EXPECT_EQ(platf::hidmaestro::normalize_absolute(2000.0F, 1920), 32767);
  EXPECT_EQ(platf::hidmaestro::normalize_absolute(10.0F, 1), 0);
}

TEST(HidMaestroMouseTest, TranslatesMoonlightButtons) {
  EXPECT_EQ(platf::hidmaestro::translate_button(BUTTON_LEFT), 1U);
  EXPECT_EQ(platf::hidmaestro::translate_button(BUTTON_RIGHT), 2U);
  EXPECT_EQ(platf::hidmaestro::translate_button(BUTTON_MIDDLE), 3U);
  EXPECT_EQ(platf::hidmaestro::translate_button(BUTTON_X1), 4U);
  EXPECT_EQ(platf::hidmaestro::translate_button(BUTTON_X2), 5U);
  EXPECT_EQ(platf::hidmaestro::translate_button(99), 0U);
}

TEST(HidMaestroMouseTest, AccumulatesAndChunksWheelDetents) {
  int remainder = 0;
  EXPECT_TRUE(platf::hidmaestro::scroll_chunks(remainder, 60).empty());
  EXPECT_EQ(remainder, 60);
  EXPECT_EQ(platf::hidmaestro::scroll_chunks(remainder, 60), std::vector<int>({1}));
  EXPECT_EQ(remainder, 0);
  EXPECT_EQ(platf::hidmaestro::scroll_chunks(remainder, 120 * 300), std::vector<int>({127, 127, 46}));
  EXPECT_EQ(platf::hidmaestro::scroll_chunks(remainder, -120 * 129), std::vector<int>({-127, -2}));
}

#endif
