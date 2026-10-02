package com.luma.browser.ui

import android.animation.ObjectAnimator
import android.animation.ValueAnimator
import android.view.LayoutInflater
import android.view.View
import android.view.ViewGroup
import android.widget.FrameLayout
import android.widget.ImageView
import android.widget.LinearLayout
import android.widget.TextView
import androidx.recyclerview.widget.RecyclerView
import com.luma.browser.R
import com.luma.browser.ai.AiMessage
import io.noties.markwon.Markwon

class AiChatAdapter(private val messages: MutableList<AiMessage>) :
    RecyclerView.Adapter<AiChatAdapter.VH>() {

    private var markwon: Markwon? = null

    inner class VH(view: View) : RecyclerView.ViewHolder(view) {
        val assistantHeader: LinearLayout = view.findViewById(R.id.msgAssistantHeader)
        val role: TextView = view.findViewById(R.id.msgRole)
        val bubble: LinearLayout = view.findViewById(R.id.msgBubble)
        val image: ImageView = view.findViewById(R.id.msgImage)
        val text: TextView = view.findViewById(R.id.msgText)
        val typingDots: LinearLayout = view.findViewById(R.id.msgTypingDots)
        val dot1: View = view.findViewById(R.id.dot1)
        val dot2: View = view.findViewById(R.id.dot2)
        val dot3: View = view.findViewById(R.id.dot3)

        var anim1: ObjectAnimator? = null
        var anim2: ObjectAnimator? = null
        var anim3: ObjectAnimator? = null

        fun startTypingAnimation() {
            stopTypingAnimation()
            anim1 = ObjectAnimator.ofFloat(dot1, "alpha", 0.25f, 1f).apply {
                duration = 500
                repeatCount = ValueAnimator.INFINITE
                repeatMode = ValueAnimator.REVERSE
                start()
            }
            anim2 = ObjectAnimator.ofFloat(dot2, "alpha", 0.25f, 1f).apply {
                duration = 500
                startDelay = 170
                repeatCount = ValueAnimator.INFINITE
                repeatMode = ValueAnimator.REVERSE
                start()
            }
            anim3 = ObjectAnimator.ofFloat(dot3, "alpha", 0.25f, 1f).apply {
                duration = 500
                startDelay = 340
                repeatCount = ValueAnimator.INFINITE
                repeatMode = ValueAnimator.REVERSE
                start()
            }
        }

        fun stopTypingAnimation() {
            anim1?.cancel()
            anim2?.cancel()
            anim3?.cancel()
            anim1 = null
            anim2 = null
            anim3 = null
            dot1.alpha = 1f
            dot2.alpha = 1f
            dot3.alpha = 1f
        }
    }

    override fun onCreateViewHolder(parent: ViewGroup, viewType: Int): VH {
        if (markwon == null) {
            markwon = Markwon.create(parent.context)
        }
        val view = LayoutInflater.from(parent.context).inflate(R.layout.item_ai_message, parent, false)
        return VH(view)
    }

    override fun onBindViewHolder(holder: VH, position: Int) {
        val msg = messages[position]
        val isUser = msg.role == "user"

        if (msg.imageBitmap != null) {
            holder.image.visibility = View.VISIBLE
            holder.image.setImageBitmap(msg.imageBitmap)
        } else {
            holder.image.visibility = View.GONE
        }

        if (isUser) {
            holder.assistantHeader.visibility = View.GONE
            holder.role.visibility = View.VISIBLE
            holder.role.text = "ВЫ"
            holder.role.textAlignment = View.TEXT_ALIGNMENT_TEXT_END

            holder.typingDots.visibility = View.GONE
            holder.stopTypingAnimation()
            holder.text.visibility = View.VISIBLE
            holder.text.text = msg.content
            holder.text.setBackgroundResource(R.drawable.bg_surface_raised)
            holder.text.setTextColor(0xFFFFFFFF.toInt())

            val params = holder.bubble.layoutParams as? ViewGroup.MarginLayoutParams
            params?.marginStart = 80
            params?.marginEnd = 0
            holder.bubble.layoutParams = params
        } else {
            holder.assistantHeader.visibility = View.VISIBLE
            holder.role.visibility = View.GONE

            val params = holder.bubble.layoutParams as? ViewGroup.MarginLayoutParams
            params?.marginStart = 0
            params?.marginEnd = 40
            holder.bubble.layoutParams = params

            if (msg.content.isBlank()) {
                // Telegram typing animation
                holder.text.visibility = View.GONE
                holder.typingDots.visibility = View.VISIBLE
                holder.startTypingAnimation()
            } else {
                holder.typingDots.visibility = View.GONE
                holder.stopTypingAnimation()
                holder.text.visibility = View.VISIBLE
                holder.text.setBackgroundResource(R.drawable.bg_glass_card)
                holder.text.setTextColor(0xFFFFFFFF.toInt())
                val m = markwon
                if (m != null) {
                    m.setMarkdown(holder.text, msg.content)
                } else {
                    holder.text.text = msg.content
                }
            }
        }
    }

    override fun onBindViewHolder(holder: VH, position: Int, payloads: MutableList<Any>) {
        if (payloads.isNotEmpty()) {
            val msg = messages[position]
            val isUser = msg.role == "user"
            if (!isUser) {
                if (msg.content.isBlank()) {
                    holder.text.visibility = View.GONE
                    holder.typingDots.visibility = View.VISIBLE
                    holder.startTypingAnimation()
                } else {
                    holder.typingDots.visibility = View.GONE
                    holder.stopTypingAnimation()
                    holder.text.visibility = View.VISIBLE
                    val m = markwon
                    if (m != null) {
                        m.setMarkdown(holder.text, msg.content)
                    } else {
                        holder.text.text = msg.content
                    }
                }
            }
            return
        }
        super.onBindViewHolder(holder, position, payloads)
    }

    override fun onViewRecycled(holder: VH) {
        super.onViewRecycled(holder)
        holder.stopTypingAnimation()
    }

    override fun getItemCount() = messages.size

    fun appendToLastAssistant(chunk: String) {
        val lastIdx = messages.indexOfLast { it.role == "assistant" }
        if (lastIdx >= 0) {
            val old = messages[lastIdx]
            messages[lastIdx] = old.copy(content = old.content + chunk)
            notifyItemChanged(lastIdx, PAYLOAD_UPDATE)
        }
    }

    fun addMessage(msg: AiMessage) {
        messages.add(msg)
        notifyItemInserted(messages.size - 1)
    }

    fun clear() {
        messages.clear()
        notifyDataSetChanged()
    }

    companion object {
        private const val PAYLOAD_UPDATE = "PAYLOAD_UPDATE"
    }
}
