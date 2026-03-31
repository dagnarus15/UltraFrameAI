cmake_minimum_required(VERSION 3.20)

project(UltraFrameAI_Encoder LANGUAGES CXX)

add_library(UltraFrameAI.Encoder.Native SHARED
    src/encoder_bridge.cpp
)

target_include_directories(UltraFrameAI.Encoder.Native
    PRIVATE
        ${CMAKE_CURRENT_SOURCE_DIR}/src
)

target_compile_features(UltraFrameAI.Encoder.Native PRIVATE cxx_std_20)

if (MSVC)
    target_compile_options(UltraFrameAI.Encoder.Native PRIVATE /W4 /permissive-)
    target_compile_definitions(UltraFrameAI.Encoder.Native PRIVATE AE_BUILD_DLL NOMINMAX)
else()
    target_compile_options(UltraFrameAI.Encoder.Native PRIVATE -Wall -Wextra -Wpedantic)
    target_compile_definitions(UltraFrameAI.Encoder.Native PRIVATE AE_BUILD_DLL)
endif()
