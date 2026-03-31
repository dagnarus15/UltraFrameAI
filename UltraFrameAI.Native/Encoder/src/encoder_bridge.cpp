#include "encoder_bridge.h"

#include <algorithm>
#include <cstring>
#include <string>
#include <vector>

namespace
{
    class EncoderSession final
    {
    public:
        explicit EncoderSession(const ae_config& config)
            : m_width(std::max(1, config.width))
            , m_height(std::max(1, config.height))
            , m_channels(config.channels == 4 ? 4 : 3)
            , m_hasAudio(config.has_audio != 0)
            , m_hasSubtitles(config.has_subtitles != 0)
            , m_frameBytes(static_cast<std::size_t>(m_width) * static_cast<std::size_t>(m_height) * static_cast<std::size_t>(m_channels))
        {
            m_lastError = "encoder backend stub: native FFmpeg path not wired yet";
        }

        void Reset()
        {
            m_frameCount = 0;
            m_lastPts = 0.0;
            m_lastError.clear();
            m_lastError = "encoder backend stub: native FFmpeg path not wired yet";
        }

        int SubmitFrame(const std::uint8_t* inputBgr, int byteCount, double ptsSeconds)
        {
            if (inputBgr == nullptr)
            {
                SetError("input frame is null");
                return -1;
            }

            if (byteCount < 0 || static_cast<std::size_t>(byteCount) < m_frameBytes)
            {
                SetError("input frame byte count is smaller than expected frame size");
                return -2;
            }

            m_lastPts = ptsSeconds;
            ++m_frameCount;
            return 0;
        }

        int Flush()
        {
            m_flushed = true;
            return 0;
        }

        const char* LastError() const
        {
            return m_lastError.c_str();
        }

    private:
        void SetError(const char* error)
        {
            m_lastError = error != nullptr ? error : "unknown encoder error";
        }

        const int m_width;
        const int m_height;
        const int m_channels;
        const bool m_hasAudio;
        const bool m_hasSubtitles;
        const std::size_t m_frameBytes;
        std::size_t m_frameCount = 0;
        double m_lastPts = 0.0;
        bool m_flushed = false;
        std::string m_lastError;
    };
}

AE_API void* ae_create_session(const ae_config* config)
{
    if (config == nullptr || config->width <= 0 || config->height <= 0 || (config->channels != 3 && config->channels != 4))
    {
        return nullptr;
    }

    try
    {
        return new EncoderSession(*config);
    }
    catch (...)
    {
        return nullptr;
    }
}

AE_API void ae_destroy_session(void* handle)
{
    delete static_cast<EncoderSession*>(handle);
}

AE_API void ae_reset_session(void* handle)
{
    if (handle == nullptr)
    {
        return;
    }

    static_cast<EncoderSession*>(handle)->Reset();
}

AE_API int ae_submit_frame(void* handle, const std::uint8_t* input_bgr, int byte_count, double pts_seconds)
{
    if (handle == nullptr)
    {
        return -1;
    }

    return static_cast<EncoderSession*>(handle)->SubmitFrame(input_bgr, byte_count, pts_seconds);
}

AE_API int ae_flush(void* handle)
{
    if (handle == nullptr)
    {
        return -1;
    }

    return static_cast<EncoderSession*>(handle)->Flush();
}

AE_API const char* ae_get_last_error(void* handle)
{
    if (handle == nullptr)
    {
        return "encoder session is null";
    }

    return static_cast<EncoderSession*>(handle)->LastError();
}
