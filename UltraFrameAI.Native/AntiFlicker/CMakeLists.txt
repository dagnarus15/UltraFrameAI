cmake_minimum_required(VERSION 3.20)

project(UltraFrameAI_AntiFlicker LANGUAGES CXX)

add_library(UltraFrameAI.AntiFlicker.Native SHARED
    src/anti_flicker.cpp
)

target_include_directories(UltraFrameAI.AntiFlicker.Native
    PRIVATE
        ${CMAKE_CURRENT_SOURCE_DIR}/src
)

target_compile_features(UltraFrameAI.AntiFlicker.Native PRIVATE cxx_std_20)

if (MSVC)
    target_compile_options(UltraFrameAI.AntiFlicker.Native PRIVATE /W4 /permissive-)
    target_compile_definitions(UltraFrameAI.AntiFlicker.Native PRIVATE AF_BUILD_DLL NOMINMAX)
else()
    target_compile_options(UltraFrameAI.AntiFlicker.Native PRIVATE -Wall -Wextra -Wpedantic)
    target_compile_definitions(UltraFrameAI.AntiFlicker.Native PRIVATE AF_BUILD_DLL)
endif()

