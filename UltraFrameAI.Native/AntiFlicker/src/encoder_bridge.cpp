#include "encoder_bridge.h"

#include <memory>
#include <new>
#include <string>

namespace
{
    constexpr const char* kNotSupportedMessage =
        "Native encoder backend is a stub. FFmpeg API integration is not linked yet.";

    struct EncoderSession final
    {
        std::string LastError = kNotSupportedMessage;
        bool Opened = false;
    };

    static int SetNotSupported(EncoderSession* session)
    {
        if (session != nullptr)
        {
            session->LastError = kNotSupportedMessage;
        }

        return UFE_STATUS_NOT_SUPPORTED;
    }

    static int SetInvalidArgument(EncoderSession* session, const char* message)
    {
        if (session != nullptr)
        {
            session->LastError = message != nullptr ? message : "Invalid argument.";
        }

        return UFE_STATUS_INVALID_ARGUMENT;
    }
}

UFE_API void* ufe_session_create()
{
    try
    {
        return new EncoderSession();
    }
    catch (...)
    {
        return nullptr;
    }
}

UFE_API void ufe_session_destroy(void* handle)
{
    delete static_cast<EncoderSession*>(handle);
}

UFE_API int ufe_session_open(void* handle, const ufe_open_config* config)
{
    auto* session = static_cast<EncoderSession*>(handle);
    if (session == nullptr || config == nullptr)
    {
        return SetInvalidArgument(session, "Session or config is null.");
    }

    if (config->width <= 0 || config->height <= 0 || config->channels <= 0)
    {
        return SetInvalidArgument(session, "Frame geometry is invalid.");
    }

    if (config->fps_num <= 0 || config->fps_den <= 0)
    {
        return SetInvalidArgument(session, "FPS ratio is invalid.");
    }

    if (config->output_path == nullptr || config->output_path[0] == '\0')
    {
        return SetInvalidArgument(session, "Output path is missing.");
    }

    session->Opened = false;
    return SetNotSupported(session);
}

UFE_API int ufe_session_submit_frame(void* handle, const ufe_frame_packet* frame)
{
    auto* session = static_cast<EncoderSession*>(handle);
    if (session == nullptr || frame == nullptr)
    {
        return SetInvalidArgument(session, "Session or frame is null.");
    }

    if (frame->data == nullptr || frame->size == 0)
    {
        return SetInvalidArgument(session, "Frame buffer is empty.");
    }

    if (frame->pts_den <= 0)
    {
        return SetInvalidArgument(session, "Frame PTS denominator is invalid.");
    }

    return SetNotSupported(session);
}

UFE_API int ufe_session_submit_timestamp(void* handle, std::int64_t pts_num, std::int64_t pts_den)
{
    auto* session = static_cast<EncoderSession*>(handle);
    if (session == nullptr)
    {
        return UFE_STATUS_INVALID_ARGUMENT;
    }

    if (pts_den <= 0)
    {
        return SetInvalidArgument(session, "Timestamp denominator is invalid.");
    }

    (void)pts_num;
    return SetNotSupported(session);
}

UFE_API int ufe_session_flush(void* handle)
{
    auto* session = static_cast<EncoderSession*>(handle);
    if (session == nullptr)
    {
        return UFE_STATUS_INVALID_ARGUMENT;
    }

    return SetNotSupported(session);
}

UFE_API int ufe_session_close(void* handle)
{
    auto* session = static_cast<EncoderSession*>(handle);
    if (session == nullptr)
    {
        return UFE_STATUS_INVALID_ARGUMENT;
    }

    session->Opened = false;
    session->LastError.clear();
    return UFE_STATUS_OK;
}

UFE_API const char* ufe_session_get_last_error(void* handle)
{
    auto* session = static_cast<EncoderSession*>(handle);
    if (session == nullptr)
    {
        return "Session handle is null.";
    }

    if (session->LastError.empty())
    {
        return "";
    }

    return session->LastError.c_str();
}
