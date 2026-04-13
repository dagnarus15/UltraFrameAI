cmake_minimum_required(VERSION 3.20)

project(UltraFrameAI_AntiFlicker VERSION 0.1.0 LANGUAGES CXX)

set(UFAF_VERSION_MAJOR ${PROJECT_VERSION_MAJOR})
set(UFAF_VERSION_MINOR ${PROJECT_VERSION_MINOR})
set(UFAF_VERSION_PATCH ${PROJECT_VERSION_PATCH})
set(UFAF_VERSION_STRING "${PROJECT_VERSION}")

configure_file(
    ${CMAKE_CURRENT_SOURCE_DIR}/src/version.rc.in
    ${CMAKE_CURRENT_BINARY_DIR}/generated/UltraFrameAI.AntiFlicker.Native.version.rc
    @ONLY
)

add_library(UltraFrameAI.AntiFlicker.Native SHARED
    src/anti_flicker.cpp
    ${CMAKE_CURRENT_BINARY_DIR}/generated/UltraFrameAI.AntiFlicker.Native.version.rc
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
    target_compile_definitions(UltraFrameAI.Encoder.Native PRIVATE UFE_BUILD_DLL NOMINMAX)
else()
    target_compile_options(UltraFrameAI.Encoder.Native PRIVATE -Wall -Wextra -Wpedantic)
    target_compile_definitions(UltraFrameAI.Encoder.Native PRIVATE UFE_BUILD_DLL)
endif()
